using System;
using System.Collections.Generic;
using System.Linq;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.CSharp.Refactoring;
using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.Semantics;
using OmniSharp.Common;
using OmniSharp.Configuration;
using OmniSharp.Parser;
using OmniSharp.Refactoring;

namespace OmniSharp.CodeIssues
{
    public class FixUsingsHandler
    {
        readonly BufferParser _bufferParser;
        readonly OmniSharpConfiguration _config;
        string _fileName;
        Logger _logger;
        List<QuickFix> _ambiguous = new List<QuickFix>();

        public FixUsingsHandler(BufferParser bufferParser, Logger logger, OmniSharpConfiguration config)
        {
            _bufferParser = bufferParser;
            _logger = logger;
            _config = config;
        }


        public FixUsingsResponse FixUsings(Request request)
        {
            _fileName = request.FileName;
            string buffer = RemoveUsings(request.Buffer);
            buffer = SortUsings(buffer);
            buffer = AddLinqForQueryIfMissing(buffer);
            var content = _bufferParser.ParsedContent(buffer, _fileName);
            var tree = content.SyntaxTree;
            OmniSharpRefactoringContext context;

            var resolver = new CSharpAstResolver(content.Compilation, content.SyntaxTree, content.UnresolvedFile);
            var node = GetNextUnresolvedNode(tree, resolver);

            while (node != null)
            {
                string oldBuffer = buffer;

                var astNode = GetNodeToAddUsing(node);
                
                request = CreateRequest(buffer, astNode);

                context = OmniSharpRefactoringContext.GetContext(_bufferParser, request);
                var action = new AddUsingAction();
                var actions = action.GetActions(context);
                using (var script = new OmniSharpScript(context, _config))
                {
                    var usingActions = actions.Where(a => a.Description.StartsWith("using"));
                    if (usingActions.Count() == 1)
                    {
                        foreach (var act in usingActions)
                        {
                            act.Run(script);
                        }
                    }
                    else
                    {
                        _ambiguous.Add(new QuickFix
                            { 
                                Column = request.Column,
                                Line = request.Line,
                                FileName = request.FileName,
                                Text = "`" + astNode + "`" + " is ambiguous"
                            });
                        node = GetNextUnresolvedNode(tree, astNode, resolver);
                        continue;
                    }
                }
                buffer = context.Document.Text;

                if (oldBuffer == buffer)
                {
                    _logger.Error("Something went wrong. oops");
                    _logger.Error(astNode);
                    node = GetNextUnresolvedNode(tree, astNode, resolver);
                    continue;
                }
                   
                content = _bufferParser.ParsedContent(buffer, request.FileName);
                resolver = new CSharpAstResolver(content.Compilation, content.SyntaxTree, content.UnresolvedFile);
                
                node = GetNextUnresolvedNode(content.SyntaxTree, resolver);
            }

            return new FixUsingsResponse(buffer, _ambiguous);
        }

        static AstNode GetNodeToAddUsing(NodeResolved node)
        {
            string name = null;
            AstNode astNode = node.Node;
            var unknownIdentifierResolveResult = node.ResolveResult as UnknownIdentifierResolveResult;
            if (unknownIdentifierResolveResult != null)
            {
                name = unknownIdentifierResolveResult.Identifier;
            }

            if (node.ResolveResult is UnknownMemberResolveResult)
            {
                name = (node.ResolveResult as UnknownMemberResolveResult).MemberName;
            }
            return astNode.Descendants.FirstOrDefault(n => n.ToString() == name);
        }

        string RunActions(OmniSharpRefactoringContext context, IEnumerable<CodeAction> actions)
        {
            using (var script = new OmniSharpScript(context, _config))
            {

                foreach (var action in actions)
                {
                    if (action != null)
                    {
                        action.Run(script);
                    }
                }
            }
            return context.Document.Text;
        }

        string RemoveUsings(string buffer)
        {
            var content = _bufferParser.ParsedContent(buffer, _fileName);
            var tree = content.SyntaxTree;
            var firstUsing = tree.Children.FirstOrDefault(IsUsingDeclaration);

            if (firstUsing != null)
            {
                var request = CreateRequest(buffer, firstUsing);
                var context = OmniSharpRefactoringContext.GetContext(_bufferParser, request);
                var redundantUsings = new RedundantUsingDirectiveIssue().GetIssues(context, null);
                if (redundantUsings.Any())
                {
                    var actions = redundantUsings.First().Actions;
                    buffer = RunActions(context, actions);
                }
            }

            return buffer;
        }

        Request CreateRequest(string buffer, AstNode node)
        {
            var request = new OmniSharp.Common.Request();
            request.Buffer = buffer;
            request.Line = node.Region.BeginLine;
            request.Column = node.Region.BeginColumn;
            request.FileName = _fileName;
            return request;
        }

        string SortUsings(string buffer)
        {
            var content = _bufferParser.ParsedContent(buffer, _fileName);
            var tree = content.SyntaxTree;
            var firstUsing = tree.Children.FirstOrDefault(IsUsingDeclaration);

            if (firstUsing != null)
            {
                var request = CreateRequest(buffer, firstUsing);
                var context = OmniSharpRefactoringContext.GetContext(_bufferParser, request);
                var actions = new SortUsingsAction().GetActions(context);
                buffer = RunActions(context, actions);

            }

            return buffer;
        }


        static NodeResolved GetNextUnresolvedNode(AstNode tree, CSharpAstResolver resolver)
        {
            return GetNextUnresolvedNode(tree, tree.FirstChild, resolver);
        }

        static NodeResolved GetNextUnresolvedNode(AstNode tree, AstNode after, CSharpAstResolver resolver)
        {
            var nodes = tree.Descendants.SkipWhile(n => n != after).Select(n => new NodeResolved
                {
                    Node = n,
                    ResolveResult = resolver.Resolve(n)
                });

            var node = nodes.FirstOrDefault(n => n.ResolveResult is UnknownIdentifierResolveResult);
            if (node == null)
            {
                node = nodes.FirstOrDefault(n => n.ResolveResult is UnknownMemberResolveResult);
            }
            return node;
        }

        string AddLinqForQueryIfMissing(string buffer)
        {
            var content = _bufferParser.ParsedContent(buffer, _fileName);
            var tree = content.SyntaxTree;
            if (!tree.Descendants.OfType<UsingDeclaration>().Any(u => u.Namespace.Equals("System.Linq")))
            {
                var linqQuery = tree.Descendants.FirstOrDefault(n => n.NodeType == NodeType.QueryClause);
                if (linqQuery != null)
                {
                    buffer = AddUsingLinq(linqQuery, buffer);
                }
            }
            return buffer;
        }

        string AddUsingLinq(AstNode astNode, string buffer)
        {
            var request = CreateRequest(buffer, astNode);
            var context = OmniSharpRefactoringContext.GetContext(_bufferParser, request);
            var script = new OmniSharpScript(context, _config);
            UsingHelper.InsertUsingAndRemoveRedundantNamespaceUsage(context, script, "System.Linq");
            return context.Document.Text;
        }

        private static bool IsUsingDeclaration(AstNode node)
        {
            return node is UsingDeclaration || node is UsingAliasDeclaration;
        }

        private class NodeResolved
        {
            public AstNode Node { get; set; }
            public ResolveResult ResolveResult { get; set; }
        }
    }
}
