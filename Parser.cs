using CFlat.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace CFlat
{
    public class Parser
    {
        private ParserState _state = ParserState.Root;

        private Ast _root;
        private Method _method;
        private Struct _struct;

        private StringBuilder _errors = new StringBuilder();
        private Token _currentToken;

        private void ReportError(ref TokenStream input, string message)
        {
            _errors.Append(input.FileName);
            _errors.Append("(");
            _errors.Append(_currentToken.LineNumber);
            _errors.Append("): ");
            _errors.AppendLine(message);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Token EatToken(ref TokenStream input, ref int tokenIndex)
        {
            return _currentToken = input.Tokens[tokenIndex++];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Token PeekToken(ref TokenStream input, int tokenIndex)
        {
            return input.Tokens[tokenIndex];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsReservedRootLevelKeyword(Token token)
        {
            return token.Equals("struct") || token.Equals("include");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsMethodDefinition(Token token)
        {
            return token.Type == TokenType.Identifier && !IsReservedRootLevelKeyword(token);
        }

        private bool ParseMethodDefinition(ref TokenStream input, ref int tokenIndex, out INode node, ref ParserState nextState)
        {
            node = null;

            var token = PeekToken(ref input, tokenIndex);
            if (!IsMethodDefinition(token))
            {
                return false;
            }

            var methodName = EatToken(ref input, ref tokenIndex);
            if (methodName.Type != TokenType.Identifier)
            {
                ReportError(ref input, "Method name is not an identifier.");
                return false;
            }

            var separator = EatToken(ref input, ref tokenIndex);
            if (separator.Type != TokenType.Operator && !separator.Equals("::"))
            {
                ReportError(ref input, "'::' expected.");
                return false;
            }

            var openBracket = EatToken(ref input, ref tokenIndex);
            if (openBracket.Type != TokenType.OpenBracket && !openBracket.Equals("("))
            {
                ReportError(ref input, "'(' expected.");
                return false;
            }

            var nextToken = EatToken(ref input, ref tokenIndex);
            
            var parameterTypeNames = new List<Token>();
            var parameterNames = new List<Token>();

            while (nextToken.Type != TokenType.CloseBracket)
            {
                if (nextToken.Type != TokenType.Identifier)
                {
                    ReportError(ref input, "Identifier expected for parameter type #" + parameterTypeNames.Count + ".");
                    return false;
                }

                parameterTypeNames.Add(nextToken);

                var parameterName = EatToken(ref input, ref tokenIndex);
                if (parameterName.Type != TokenType.Identifier)
                {
                    ReportError(ref input, "Identifier expected for parameter name #" + parameterNames.Count + ".");
                    return false;
                }

                parameterNames.Add(parameterName);

                var comma = PeekToken(ref input, tokenIndex);
                if (comma.Type == TokenType.Comma)
                {
                    EatToken(ref input, ref tokenIndex);
                }

                nextToken = EatToken(ref input, ref tokenIndex);
            }

            var closeBracket = nextToken;
            if (closeBracket.Type != TokenType.CloseBracket && !closeBracket.Equals(")"))
            {
                ReportError(ref input, "')' expected.");
                return false;
            }

            Token returnTypeName = default;

            var returnArrow = PeekToken(ref input, tokenIndex);
            if (returnArrow.Type == TokenType.Operator && returnArrow.Equals("->"))
            {
                tokenIndex++;

                var returnType = EatToken(ref input, ref tokenIndex);
                if (returnType.Type != TokenType.Identifier)
                {
                    ReportError(ref input, "Method return type: Identifier expected.");
                    return false;
                }

                returnTypeName = returnType;
            }

            var methodScope = EatToken(ref input, ref tokenIndex);
            if (methodScope.Type != TokenType.OpenBracket && !methodScope.Equals("{"))
            {
                ReportError(ref input, "'{' expected.");
                return false;
            }

            _method = new Method
            {
                Root = _root,
                ReturnTypeName = returnTypeName,
                MethodName = methodName,
                ParameterNames = parameterNames,
                ParameterTypeNames = parameterTypeNames
            };

            nextState = ParserState.Method;
            node = _method;

            return true;
        }

        private bool ParseStructDefinition(ref TokenStream input, ref int tokenIndex, out INode node, ref ParserState nextState)
        {
            node = null;

            var structSymbol = PeekToken(ref input, tokenIndex);
            if (!structSymbol.Equals("struct"))
            {
                return false;
            }

            tokenIndex++;

            var structName = EatToken(ref input, ref tokenIndex);
            if (structName.Type != TokenType.Identifier)
            {
                ReportError(ref input, "Structs need identifiers as their name.");
                return false;
            }

            var openBracket = EatToken(ref input, ref tokenIndex);
            if (openBracket.Type != TokenType.OpenBracket || !openBracket.Equals("{"))
            {
                ReportError(ref input, "'{' expected.");
                return false;
            }

            nextState = ParserState.Struct;
            node = _struct = new Struct
            {
                StructName = structName
            };

            return true;
        }

        private bool ParseStructField(ref TokenStream input, ref int tokenIndex, out INode node)
        {
            node = null;

            var fieldType = EatToken(ref input, ref tokenIndex);
            if (fieldType.Type != TokenType.Identifier)
            {
                ReportError(ref input, "Expected identifier for field type.");
                return false;
            }

            var fieldName = EatToken(ref input, ref tokenIndex);
            if (fieldName.Type != TokenType.Identifier)
            {
                ReportError(ref input, "Expected identifier for field name.");
                return false;
            }

            var semicolon = EatToken(ref input, ref tokenIndex);
            if (semicolon.Type != TokenType.Semicolon)
            {
                ReportError(ref input, "';' expected.");
                return false;
            }

            node = new Field
            {
                FieldName = fieldName,
                FieldType = fieldType
            };

            return true;
        }

        private bool ParseInclude(ref TokenStream input, ref int tokenIndex, out INode node)
        {
            node = null;

            var includeSymbol = PeekToken(ref input, tokenIndex);
            if (includeSymbol.Type != TokenType.Identifier || !includeSymbol.Equals("include"))
                return false;

            tokenIndex++;

            var name = EatToken(ref input, ref tokenIndex);
            if (name.Type != TokenType.String)
            {
                ReportError(ref input, "String expected.");
                return false;
            }

            var semicolon = EatToken(ref input, ref tokenIndex);
            if (semicolon.Type != TokenType.Semicolon)
            {
                ReportError(ref input, "';' expected.");
                return false;
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(input.FileName));
            var path = Path.Combine(directory, name.ToString() + ".cb");

            if (!File.Exists(path))
            {
                ReportError(ref input, "Could not include: '" + path + "'. File not found.");
                return false;
            }

            var file = new StringBuilder(File.ReadAllText(path));

            var tokens = Tokenizer.Tokenize(file, 0, file.Length);
            tokens.FileName = path;

            var includeParser = new Parser();
            var included = includeParser.Parse(tokens, out var errors);

            if (errors.Length > 0)
            {
                _errors.Append(errors.ToString());
            }

            node = new Include();
            foreach (var child in included.Children)
            {
                node.Add(child);
            }

            return true;
        }

        private bool ParseLocalAssignment(ref TokenStream input, ref int tokenIndex, out INode node)
        {
            node = null;

            var variableName = PeekToken(ref input, tokenIndex);
            if (variableName.Type != TokenType.Identifier)
                return false;

            return true;
        }

        private bool ParseLocalDeclarationOrAssignment(ref TokenStream input, ref int tokenIndex, out INode node)
        {
            node = null;

            var localName = PeekToken(ref input, tokenIndex);
            if (localName.Type != TokenType.Identifier)
                return false;
            
            var assignmentOperator = PeekToken(ref input, tokenIndex + 1);
            if (assignmentOperator.Type != TokenType.Operator || !assignmentOperator.Equals(":="))
                return false;

            tokenIndex += 2;

            INode expression = null;
            if (!ParseExpression(ref input, ref tokenIndex, out expression, out var foundExpression) && !foundExpression)
            {
                ReportError(ref input, "Local declaration '" + localName + " := (...)' is missing it's right hand side.");
                return false;
            }

            node = new LocalAssignment
            {
                LocalName = localName,
                Expression = (Expression)expression
            };

            expression.Parent = node;

            return true;
        }

        private bool IsEndOfExpression(ref TokenStream input, ref int tokenIndex)
        {
            var peek = PeekToken(ref input, tokenIndex);
            var result = peek.Type == TokenType.Semicolon || 
                peek.Type == TokenType.Comma ||
                peek.Type == TokenType.CloseBracket;
            
            return result;
        }

        private int GetOperatorPrecedence(Token op)
        {
            if (op.Equals("+") || op.Equals("-"))
                return 1;

            if (op.Equals("*") || op.Equals("/"))
                return 2;

            if (op.Equals("==") || op.Equals("!="))
                return 3;

            if (op.Equals("&") || op.Equals("&&") || op.Equals("|") || op.Equals("||"))
                return 4;

            return -1;
        }

        private bool ParseSubExpression(ref TokenStream input, ref int tokenIndex, out INode node)
        {
            return ParseIntervalSymbol(ref input, ref tokenIndex, out node) ||
                   ParseNoteSequence(ref input, ref tokenIndex, out node) ||
                   ParseNoteSequenceVariation(ref input, ref tokenIndex, out node) ||
                   ParseConstant(ref input, ref tokenIndex, out node) ||
                   ParsePlaceholder(ref input, ref tokenIndex, out node) ||
                   ParseVariableDereference(ref input, ref tokenIndex, out node) ||
                   ParseYield(ref input, ref tokenIndex, out node) ||
                   ParseMethodCall(ref input, ref tokenIndex, out node);
        }

        private bool ParseVariableDereference(ref TokenStream input, ref int tokenIndex, out INode node)
        {
            node = null;

            var identifier = PeekToken(ref input, tokenIndex);
            if (identifier.Type != TokenType.Identifier)
                return false;

            var openBracket = PeekToken(ref input, tokenIndex + 1);
            if (openBracket.Type == TokenType.OpenBracket)
                return false;

            var path = new List<Token>();

            var nextToken = EatToken(ref input, ref tokenIndex);
            while (nextToken.Type == TokenType.Identifier)
            {
                path.Add(nextToken);

                var peekDot = PeekToken(ref input, tokenIndex);
                if (peekDot.Type == TokenType.Dot)
                {
                    EatToken(ref input, ref tokenIndex);
                    var nextNextToken = PeekToken(ref input, tokenIndex);
                    if (nextNextToken.Type != TokenType.Identifier)
                    {
                        ReportError(ref input, "Dereference can not end with '.': Identifier expected.");
                        return false;
                    }
                }
                else
                {
                    break;
                }

                nextToken = EatToken(ref input, ref tokenIndex);
            }

            node = new LocalDereference
            {
                Path = path
            };

            return true;
        }

        private bool ParseYield(ref TokenStream input, ref int tokenIndex, out INode node)
        {
            node = null;

            var yieldOperator = PeekToken(ref input, tokenIndex);
            if (yieldOperator.Type != TokenType.Operator || !yieldOperator.Equals("<<"))
                return false;

            tokenIndex++;

            if (!ParsePossibleInMethodScope(ref input, ref tokenIndex, out var parameter))
                return false;

            node = new MethodCall { MethodName = yieldOperator };
            node.Add(parameter);

            return true;
        }

        private bool ParseExpression(ref TokenStream input, ref int tokenIndex, out INode node)
        {
            return ParseExpression(ref input, ref tokenIndex, out node, out var _);
        }

        private bool ParseExpression(ref TokenStream input, ref int tokenIndex, out INode node, out bool foundExpression)
        {
            node = null;

            foundExpression = ParseSubExpression(ref input, ref tokenIndex, out var firstOperand);
            if (!foundExpression)
                return false;

            node = new Expression { Root = _root };
            node.Add(firstOperand);

            var stack = new Stack<NodeWithTokenType>();            
            var bracketDepth = 0;

            while (!IsEndOfExpression(ref input, ref tokenIndex))
            {
                var currentToken = PeekToken(ref input, tokenIndex);
                if (currentToken.Type != TokenType.Operator)
                {
                    return false;
                }

                tokenIndex++;

                var op = new Operator(currentToken) { Root = _root };

                if (stack.Count > 0)
                {
                    var peek = stack.Peek();
                    var peekOp = peek.Node as Operator;

                    while (stack.Count > 0 &&
                           GetOperatorPrecedence(peekOp.Token) > GetOperatorPrecedence(op.Token) &&
                           peek.Type != TokenType.OpenBracket)
                    {
                        var nodeWithToken = stack.Pop();
                        if (nodeWithToken.Node == null)
                            continue;

                        node.Add(nodeWithToken.Node);
                    }
                }

                stack.Push(new NodeWithTokenType { Node = op, Type = TokenType.Operator });

                var bracket = PeekToken(ref input, tokenIndex);
                if (bracket.Type == TokenType.OpenBracket && bracket.Equals("("))
                {
                    bracketDepth++;
                    tokenIndex++;
                    stack.Push(new NodeWithTokenType { Type = TokenType.OpenBracket });
                    continue;
                }
                else if (bracket.Type == TokenType.CloseBracket && bracket.Equals(")"))
                {
                    bracketDepth--;
                    tokenIndex++;
                    
                    while (stack.Peek().Type != TokenType.OpenBracket)
                    {
                        node.Add(stack.Pop().Node);
                    }

                    stack.Pop();
                    continue;
                }

                var hasOperand = ParseSubExpression(ref input, ref tokenIndex, out var operand);
                if (!hasOperand)
                {
                    ReportError(ref input, "Expressions need to end with a constant or a call, not with an operator.");
                    return false;
                }

                node.Add(operand);

                currentToken = PeekToken(ref input, tokenIndex);
                if (currentToken.Type == TokenType.Semicolon || currentToken.Type == TokenType.Comma || currentToken.Type == TokenType.CloseBracket)
                {
                    break;
                }
            }

            while (stack.Count > 0)
            {
                var nodeWithTokenType = stack.Pop();
                if (nodeWithTokenType.Node == null)
                    continue;

                node.Add(nodeWithTokenType.Node);
            }

            return true;
        }

        private bool ParsePlaceholder(ref TokenStream input, ref int tokenIndex, out INode node)
        {
            node = null;

            var peek = PeekToken(ref input, tokenIndex);
            if (peek.Type != TokenType.QuestionMark && peek.Type != TokenType.NoteHead)
                return false;

            tokenIndex++;

            float rhythm = -1;
            INode subExpression = null;

            if (peek.Type == TokenType.NoteHead)
            {
                var nextToken = PeekToken(ref input, tokenIndex);
                if (nextToken.Type != TokenType.QuestionMark)
                    return false;

                tokenIndex++;
                rhythm = DurationFromNoteHead(peek);
            }
            else if (peek.Type == TokenType.QuestionMark)
            {
                ParseSubExpression(ref input, ref tokenIndex, out subExpression);
            }

            node = new Placeholder
            {
                Root = _root,
                StartToken = peek,
                Duration = rhythm,
                PitchExpression = subExpression
            };

            if (subExpression != null)
            {
                subExpression.Parent = node;
            }

            return true;
        }

        private float DurationFromNoteHead(Token noteHead)
        {
            float duration = 0;

            if (noteHead.Equals("𝅝"))
            {
                duration = 1.0f;
            }
            else if (noteHead.Equals("𝅗𝅥"))
            {
                duration = 0.5f;
            }
            else if (noteHead.Equals("𝅘𝅥"))
            {
                duration = 0.25f;
            }
            else if (noteHead.Equals("𝅘𝅥𝅮"))
            {
                duration = 0.125f;
            }
            else if (noteHead.Equals("𝅘𝅥𝅯"))
            {
                duration = 0.5f * 0.125f;
            }

            return duration;
        }

        private bool ParseNoteSequence(ref TokenStream input, ref int tokenIndex, out INode node)
        {
            node = null;
            var noteHead = PeekToken(ref input, tokenIndex);
            if (noteHead.Type != TokenType.NoteHead)
                return false;

            var questionMark = PeekToken(ref input, tokenIndex + 1);
            if (questionMark.Type == TokenType.QuestionMark)
                return false;
            
            var sequence = new NoteSequence { Root = _root };

            while (true)
            {
                noteHead = PeekToken(ref input, tokenIndex);
                if (noteHead.Type != TokenType.NoteHead)
                {
                    break;
                }

                tokenIndex++;

                INode subExpression;
                if (!(ParseIntervalSymbol(ref input, ref tokenIndex, out subExpression) ||
                      ParsePlaceholder(ref input, ref tokenIndex, out subExpression) ||
                      ParseConstant(ref input, ref tokenIndex, out subExpression) ||
                      ParseVariableDereference(ref input, ref tokenIndex, out subExpression) ||
                      ParseMethodCall(ref input, ref tokenIndex, out subExpression)))
                {
                    return false;
                }

                var duration = DurationFromNoteHead(noteHead);

                var expression = new Expression { Root = _root };
                expression.Add(subExpression);

                var note = new Note
                {
                    Root = _root,
                    Duration = duration,
                    Expression = expression
                };

                expression.Parent = note;

                sequence.Add(note);
            }

            node = sequence;
            return true;
        }

        private bool ParseNoteSequenceVariation(ref TokenStream input, ref int tokenIndex, out INode node)
        {
            node = null;

            var sourceIdentifier = PeekToken(ref input, tokenIndex);
            if (sourceIdentifier.Type != TokenType.Identifier)
                return false;

            var openCurlyBracket = PeekToken(ref input, tokenIndex + 1);
            if (openCurlyBracket.Type != TokenType.OpenBracket || !openCurlyBracket.Equals("{"))
                return false;

            tokenIndex += 2;

            var expressions = new List<Expression>();

            while (true)
            {
                if (!ParseExpression(ref input, ref tokenIndex, out var expression))
                {
                    ReportError(ref input, "Note sequence: Could not parse expression.");
                    return false;
                }

                expressions.Add((Expression)expression);
                
                var peek = PeekToken(ref input, tokenIndex);
                if (peek.Type == TokenType.CloseBracket && peek.Equals("}"))
                {
                    tokenIndex++;
                    break;
                }

                var comma = PeekToken(ref input, tokenIndex);
                if (comma.Type != TokenType.Comma)
                {
                    ReportError(ref input, "',' expected.");
                    return false;
                }
                else
                {
                    tokenIndex++;
                }
            }

            node = new NoteSequenceVariation
            {
                Root = _root,
                SourceIdentifier = sourceIdentifier,
                Expressions = expressions
            };

            for (int i = 0; i < expressions.Count; i++)
            {
                expressions[i].Parent = node;
            }

            return true;
        }

        private bool ParseIntervalSymbol(ref TokenStream input, ref int tokenIndex, out INode node)
        {
            node = null;

            var token = PeekToken(ref input, tokenIndex);
            if (token.Type != TokenType.Keyword)
                return false;

            tokenIndex++;

            int interval = 0;
            switch (token.ToString())
            {
                case "root": interval = 0; break;
                case "2nd": interval = 2; break;
                case "3rd": interval = 4; break;
                case "4th": interval = 5; break;
                case "5th": interval = 7; break;
                case "6th": interval = 9; break;
                case "7th": interval = 11; break;
                case "octave": interval = 12; break;
            }

            token.Substitution = interval.ToString();

            node = new Constant
            {
                Root = _root,
                Type = ConstantType.Int,
                Value = token
            };

            return true;
        }

        private bool ParseConstant(ref TokenStream input, ref int tokenIndex, out INode node)
        {
            node = null;

            var token = PeekToken(ref input, tokenIndex);
            if (token.Type == TokenType.Number)
            {
                node = new Constant
                {
                    Type = ConstantType.Int,
                    Value = token
                };

                tokenIndex++;
                return true;
            }
            if (token.Type == TokenType.String)
            {
                node = new Constant
                {
                    Type = ConstantType.String,
                    Value = token
                };

                tokenIndex++;
                return true;
            }

            return false;
        }

        private bool ParseReturn(ref TokenStream input, ref int tokenIndex, out INode node)
        {
            node = null;

            var returnSymbol = PeekToken(ref input, tokenIndex);
            if (returnSymbol.Type != TokenType.Identifier || !returnSymbol.Equals("return"))
                return false;

            tokenIndex++;

            node = new Return();

            var hasExpression = ParseExpression(ref input, ref tokenIndex, out var expression);
            if (hasExpression)
            {
                node.Add(expression);
            }

            return true;
        }

        private bool ParseMethodCall(ref TokenStream input, ref int tokenIndex, out INode node)
        {
            node = null;

            var methodName = PeekToken(ref input, tokenIndex);
            if (methodName.Type != TokenType.Identifier)
                return false;

            var openBracket = PeekToken(ref input, tokenIndex + 1);
            if (openBracket.Type != TokenType.OpenBracket || !openBracket.Equals("("))
                return false;

            tokenIndex += 2;

            INode expression;
            var parameters = new List<INode>(32);

            while (ParseExpression(ref input, ref tokenIndex, out expression))
            {
                parameters.Add(expression);

                var peek = PeekToken(ref input, tokenIndex);
                if (peek.Type == TokenType.Comma)
                {
                    EatToken(ref input, ref tokenIndex);
                }
            }

            var closeBracket = EatToken(ref input, ref tokenIndex);
            if (closeBracket.Type != TokenType.CloseBracket || !closeBracket.Equals(")"))
            {
                ReportError(ref input, "')' expected.");
                return false;
            }

            node = new MethodCall
            {
                Root = _root,
                MethodName = methodName
            };

            foreach (var parameter in parameters)
            {
                node.Add(parameter);
            }

            return true;
        }

        private bool ParsePossibleInMethodScope(ref TokenStream input, ref int tokenIndex, out INode node)
        {
            return ParseLocalDeclarationOrAssignment(ref input, ref tokenIndex, out node) ||
                   ParseReturn(ref input, ref tokenIndex, out node) ||
                   ParseExpression(ref input, ref tokenIndex, out node);
        }

        private bool Parse(ref TokenStream input, ref int tokenIndex, out INode node, ref ParserState nextState)
        {
            switch (_state)
            {
                case ParserState.Root:
                    {
                        return ParseInclude(ref input, ref tokenIndex, out node) ||
                               ParseStructDefinition(ref input, ref tokenIndex, out node, ref nextState) ||
                               ParseMethodDefinition(ref input, ref tokenIndex, out node, ref nextState);
                    }
                case ParserState.Struct:
                    {
                        var peek = PeekToken(ref input, tokenIndex);
                        if (peek.Type == TokenType.CloseBracket && peek.Equals("}"))
                        {
                            tokenIndex++;

                            node = null;
                            nextState = ParserState.Root;

                            return true;
                        }

                        return ParseStructField(ref input, ref tokenIndex, out node);
                    }
                case ParserState.Method:
                    {
                        var peek = PeekToken(ref input, tokenIndex);
                        if (peek.Type == TokenType.CloseBracket && peek.Equals("}"))
                        {
                            tokenIndex++;

                            node = null;
                            nextState = ParserState.Root;

                            return true;
                        }

                        var result = ParsePossibleInMethodScope(ref input, ref tokenIndex, out node);

                        if (result)
                        {
                            var semicolon = PeekToken(ref input, tokenIndex);
                            if (semicolon.Type != TokenType.Semicolon)
                            {
                                ReportError(ref input, "';' expected.");
                                return false;
                            }

                            tokenIndex++;
                        }

                        return result;
                    }
            }

            node = null;
            return false;
        }

        public Ast Parse(TokenStream input, out StringBuilder errors)
        {
            _root = new Ast { FileName = input.FileName };
            _state = ParserState.Root;
            _errors.Clear();

            int tokenIndex = input.StartIndex;
            int endIndex = input.StartIndex + input.Count;

            INode nextNode;
            ParserState nextState = _state;

            while (tokenIndex < endIndex && Parse(ref input, ref tokenIndex, out nextNode, ref nextState))
            {
                if (nextNode != null)
                {
                    switch (_state)
                    {
                        case ParserState.Root:
                            _root.Add(nextNode);
                            break;
                        case ParserState.Method:
                            _method.Add(nextNode);
                            break;
                        case ParserState.Struct:
                            _struct.Add(nextNode);
                            break;
                    }
                }

                _state = nextState;
            }

            if (tokenIndex < endIndex)
            {
                var currentToken = PeekToken(ref input, tokenIndex);
                if (currentToken.Type != TokenType.Semicolon)
                {
                    ReportError(ref input, "Not sure what to do with '" + currentToken + "'.");
                }
            }

            errors = _errors;
            return _root;
        }
    }
}
