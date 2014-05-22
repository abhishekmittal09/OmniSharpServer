using System;
using System.Collections.Generic;
using System.Linq;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.CSharp.Refactoring;
using ICSharpCode.NRefactory.CSharp.Resolver;
using ICSharpCode.NRefactory.Semantics;
using OmniSharp.Configuration;
using OmniSharp.Parser;
using OmniSharp.Refactoring;

namespace OmniSharp.CodeIssues
{
    public class FixUsingsResponse
    {
        public FixUsingsResponse(string buffer)
        {
            Buffer = buffer;
        }

        public string Buffer { get; private set; }
    }


    public class FixUsingsHandler
    {
        private readonly BufferParser _bufferParser;
        private readonly OmniSharpConfiguration _config;
        private string _fileName;

        public FixUsingsHandler(BufferParser bufferParser, OmniSharpConfiguration config)
        {
            _bufferParser = bufferParser;
            _config = config;
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
                var request = new OmniSharp.Common.Request();
                request.Buffer = buffer;
                request.Line = firstUsing.StartLocation.Line + 1;
                request.Column = firstUsing.StartLocation.Column;
                request.FileName = _fileName;
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

        string SortUsings(string buffer)
        {
            var content = _bufferParser.ParsedContent(buffer, _fileName);
            var tree = content.SyntaxTree;
            var firstUsing = tree.Children.FirstOrDefault(IsUsingDeclaration);

            if (firstUsing != null)
            {
                var request = new OmniSharp.Common.Request();
                request.Buffer = buffer;
                request.Line = firstUsing.StartLocation.Line + 1;
                request.Column = firstUsing.StartLocation.Column;
                request.FileName = _fileName;
                var context = OmniSharpRefactoringContext.GetContext(_bufferParser, request);
                var actions = new SortUsingsAction().GetActions(context);
                buffer = RunActions(context, actions);
            }

            return buffer;
        }
        
        static NodeResolved GetFirstUnresolvedNode(AstNode tree, CSharpAstResolver resolver)
        {
            var nodes = tree.Descendants.Select(n => new NodeResolved {
                Node = n,
                ResolveResult = resolver.Resolve(n)
            });
            
            var node = nodes.LastOrDefault(n => n.ResolveResult is UnknownIdentifierResolveResult);
            if(node == null)
            {
                node = nodes.LastOrDefault(n => n.ResolveResult is UnknownMemberResolveResult);
            }
            return node;
        }

        public FixUsingsResponse FixUsings(OmniSharp.Common.Request request)
        {
            _fileName = request.FileName;
            Console.WriteLine(_fileName);
            string buffer = RemoveUsings(request.Buffer);
            buffer = SortUsings(buffer);

            var content = _bufferParser.ParsedContent(buffer, _fileName);
            var tree = content.SyntaxTree;
            //var typeSystem = tree.ToTypeSystem();
            var resolver = new CSharpAstResolver(content.Compilation, content.SyntaxTree, content.UnresolvedFile);
            //System.Console.WriteLine(unresolved.Count());
            
            foreach (var nodey in tree.Descendants.Reverse())
            {

                var res = resolver.Resolve(nodey);
                System.Console.WriteLine(res);
                System.Console.WriteLine(nodey);
            }
           var node = GetFirstUnresolvedNode(tree, resolver);
            string oldBuffer = null;
            while (node != null && oldBuffer != buffer)
            {
                
                oldBuffer = buffer;
                System.Console.WriteLine("*****");
                //System.Console.WriteLine(node.Node);
//System.Console.WriteLine(node.ResolveResult.ToString());
//System.Console.WriteLine(node.NodeType);
                //}
                //foreach(var node in unresolved)
                //{
                //if (result is UnknownIdentifierResolveResult)
                {
                    //System.Console.WriteLine(node.ToString());

                    AstNode astNode = node.Node;
                    if (node.ResolveResult is UnknownIdentifierResolveResult)
                    {

                        var methodName = (node.ResolveResult as UnknownIdentifierResolveResult).Identifier;
                        //System.Console.WriteLine(methodName);
                        //System.Console.WriteLine(methodName);
                        //foreach(var x in astNode.Descendants)
                        //{
                        //System.Console.WriteLine(x);
                            
                        //}
                        astNode = astNode.Descendants.FirstOrDefault(n => n.ToString() == methodName);
                        Console.WriteLine(astNode.GetRegion().BeginColumn);
                        //while (astNode.ToString() != methodName)
                        //{
                        //astNode = astNode.NextSibling;
                        Console.WriteLine(astNode);
                        //}
                    }
                    if (node.ResolveResult is UnknownMemberResolveResult)
                    {

                        var methodName = (node.ResolveResult as UnknownMemberResolveResult).MemberName;
                        //System.Console.WriteLine(methodName);
                        //System.Console.WriteLine(methodName);
                        //foreach(var x in astNode.Descendants)
                        //{
                        //System.Console.WriteLine(x);
                        //System.Console.WriteLine(x.GetRegion().BeginColumn);
                            
                        //}
                        astNode = astNode.Descendants.FirstOrDefault(n => n.ToString() == methodName);
                        //while (astNode.ToString() != methodName)
                        //{
                        //astNode = astNode.NextSibling;
                        //System.Console.WriteLine(astNode.ToString());
                        //}
                    }
                    request.Buffer = buffer;
                    request.Line = astNode.Region.BeginLine;
                    request.Column = astNode.Region.BeginColumn ;

                    //System.Console.WriteLine(node.Node.LastChild);
                    //System.Console.WriteLine(request.Line);
                    System.Console.WriteLine(request.Column);
                    var context = OmniSharpRefactoringContext.GetContext(_bufferParser, request);
                    var action = new AddUsingAction();
                    var actions2 = action.GetActions(context);
                    using (var script = new OmniSharpScript(context, _config))
                    {
                        foreach (var action2 in actions2)
                        {
                        if (action2 != null)
                        {
                        System.Console.WriteLine(action2.Description);
                        }
                        }
                        if (actions2.Any())
                            actions2.First().Run(script);
                    }
                    buffer = context.Document.Text;
                    if(oldBuffer == buffer)
                    {
                        Console.WriteLine("Something went wrong. oops");
                        Console.WriteLine(astNode);
                    }
                   
                    content = _bufferParser.ParsedContent(buffer, request.FileName);
                    resolver = new CSharpAstResolver(content.Compilation, content.SyntaxTree, content.UnresolvedFile);
                }
           node = GetFirstUnresolvedNode(content.SyntaxTree, resolver);
            }

            return new FixUsingsResponse(buffer);
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
