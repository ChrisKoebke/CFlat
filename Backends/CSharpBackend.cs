using CFlat.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CSharp;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static CFlat.Backends.CSharpBackend.Runtime;
using static CFlat.Backends.CSharpBackend.Runtime.api;

namespace CFlat.Backends
{
    public class CSharpBackend : IBackend
    {        
        public void Load()
        {
            // Warm up Roslyn:
            Compile(new Ast(), new StringBuilder());
        }

        public Type Compile(Ast ast, StringBuilder errors)
        {
            var sourceCode = Emit(ast, errors);

#if DEBUG
            File.WriteAllText(ast.FileName + ".output.cs", sourceCode, Encoding.Default);
#endif

            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var stream = new MemoryStream();

            var result = CSharpCompilation.Create("Program")
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                .AddReferences(MetadataReference.CreateFromFile(typeof(api).Assembly.Location))
                .AddSyntaxTrees(syntaxTree)
                .Emit(stream);

            if (!result.Success)
            {
                foreach (var diagnostics in result.Diagnostics)
                {
                    errors.AppendLine(diagnostics.GetMessage()
                        .Replace("CSharpBackend.Runtime.api.", string.Empty)
                        .Replace("CSharpBackend.Runtime.", string.Empty));
                }

                return null;
            }
            else
            {
                var assembly = Assembly.Load(stream.ToArray());
                return assembly?.GetType("program");
            }
        }

        private int _currentGlobalId;

        public int GetNewGlobalId()
        {
            return _currentGlobalId++;
        }
        
        private Stack<HashSet<string>> _scope = new Stack<HashSet<string>>();

        public void PushScope()
        {
            _scope.Push(new HashSet<string>());
        }

        public void PopScope()
        {
            _scope.Pop();
        }
        public string ConvertBuiltinType(Token token)
        {
            if (token.Equals("f32"))
                return "float";
            else if (token.Equals("f64"))
                return "double";
            else if (token.Equals("i16"))
                return "short";
            else if (token.Equals("i32"))
                return "int";
            else if (token.Equals("i64"))
                return "long";

            return token.ToString();
        }

        private static Dictionary<string, string> MethodReplacements = new Dictionary<string, string>
        {
            ["using"] = "__using",
            ["<<"] = "__seq.append"
        };

        public string Emit(INode node, StringBuilder errors)
        {
            var builder = new StringBuilder();
            Emit(builder, node, errors);

            return builder.ToString();
        }

        public void Emit(StringBuilder builder, INode node, StringBuilder errors)
        {
            switch (node.NodeType)
            {
                default:
                    {
                        for (int i = 0; i < node.Children.Count; i++)
                        {
                            Emit(builder, node.Children[i], errors);
                        }
                    }
                    break;
                case NodeType.Ast:
                    {
                        builder.Append("using static CFlat.Backends.CSharpBackend.Runtime;\n\n");
                        builder.Append("using static CFlat.Backends.CSharpBackend.Runtime.api;\n\n");

                        builder.Append("public static class program {\n");
                        for (int i = 0; i < node.Children.Count; i++)
                        {
                            Emit(builder, node.Children[i], errors);
                        }
                        builder.Append("}");
                    }
                    break;
                case NodeType.Constant:
                    {
                        var constant = (Constant)node;
                        if (constant.Type == ConstantType.String)
                        {
                            builder.Append('"');
                        }

                        builder.Append(constant.Value);

                        if (constant.Type == ConstantType.String)
                        {
                            builder.Append('"');
                        }
                        else if (constant.Type == ConstantType.Float)
                        {
                            builder.Append("f");
                        }
                    }
                    break;
                case NodeType.Expression:
                    {
                        var stack = new Stack<INode>();

                        for (int i = 0; i < node.Children.Count; i++)
                        {
                            if (node.Children[i].NodeType == NodeType.Operator)
                            {
                                var op = (Operator)node.Children[i];

                                if (stack.Count >= 2)
                                {
                                    var left = stack.Pop();
                                    var right = stack.Pop();

                                    Emit(builder, right, errors);
                                    builder.Append(op.Token);
                                    Emit(builder, left, errors);
                                }
                                else if (stack.Count >= 1)
                                {
                                    var left = stack.Pop();
                                    builder.Append(op.Token);
                                    Emit(builder, left, errors);
                                }
                            }
                            else
                            {
                                stack.Push(node.Children[i]);
                            }
                        }

                        while (stack.Count > 0)
                        {
                            Emit(builder, stack.Pop(), errors);
                        }
                    }
                    break;
                case NodeType.Include:
                    {
                        for (int i = 0; i < node.Children.Count; i++)
                        {
                            Emit(builder, node.Children[i], errors);
                        }
                    }
                    break;
                case NodeType.LocalAssignment:
                    {
                        var scope = _scope.Peek();
                        var assignment = (LocalAssignment)node;
                        var localName = assignment.LocalName.ToString().Replace("'", "__v");

                        if (!scope.Contains(localName))
                        {
                            builder.Append("var ");
                        }

                        builder.Append(localName);
                        builder.Append(" = ");
                        Emit(builder, assignment.Expression, errors);

                        scope.Add(localName);
                    }
                    break;
                case NodeType.LocalDeclaration:
                    {

                    }
                    break;
                case NodeType.LocalDereference:
                    {
                        var dereference = (LocalDereference)node;
                        for (int i = 0; i < dereference.Path.Count; i++)
                        {
                            var component = dereference.Path[i].ToString();
                            if (i == 0)
                            {
                                component = component.Replace("'", "__v");
                            }
                            builder.Append(component);
                            if (i < dereference.Path.Count - 1)
                            {
                                builder.Append(".");
                            }
                        }
                    }
                    break;
                case NodeType.Method:
                    {
                        var method = (Method)node;
                        builder.Append("public static ");

                        var returnType = !method.ReturnTypeName.IsEmpty ?
                            ConvertBuiltinType(method.ReturnTypeName) :
                            "void";

                        builder.Append(returnType);
                        builder.Append(" ");
                        builder.Append(method.MethodName);
                        builder.Append("(");

                        for (int i = 0; i < method.ParameterNames?.Count; i++)
                        {
                            var parameterTypeName = ConvertBuiltinType(method.ParameterTypeNames[i]);
                            builder.Append(parameterTypeName);

                            builder.Append(" ");
                            builder.Append(method.ParameterNames[i]);

                            if (i < method.ParameterNames.Count - 1)
                            {
                                builder.Append(", ");
                            }
                        }

                        builder.Append(") {\n");
                        PushScope();

                        if (method.ReturnTypeName.Equals(nameof(seq)))
                        {
                            builder.Append("var __seq = ");
                            builder.Append(nameof(seq));
                            builder.Append(".");
                            builder.Append(nameof(seq.create));
                            builder.Append("();\n");

                            _scope.Peek().Add("__seq");
                        }

                        for (int i = 0; i < method.Children.Count; i++)
                        {
                            Emit(builder, method.Children[i], errors);
                            builder.Append(";\n");
                        }

                        if (method.ReturnTypeName.Equals(nameof(seq)))
                        {
                            builder.Append("return __seq;\n");
                        }

                        PopScope();
                        builder.Append("}\n\n");
                    }
                    break;
                case NodeType.MethodCall:
                    {
                        var methodCall = (MethodCall)node;

                        var methodName = methodCall.MethodName.ToString();
                        if (MethodReplacements.TryGetValue(methodName, out var replacementName))
                        {
                            methodName = replacementName;
                        }

                        builder.Append(methodName);
                        builder.Append("(");
                        for (int i = 0; i < node.Children.Count; i++)
                        {
                            Emit(builder, node.Children[i], errors);
                            if (i < node.Children.Count - 1)
                            {
                                builder.Append(", ");
                            }
                        }

                        builder.Append(")");
                    }
                    break;
                case NodeType.NoteSequence:
                    {
                        var sequence = (NoteSequence)node;

                        builder.Append(nameof(seq));
                        builder.Append(".");
                        builder.Append(nameof(seq.from_n_notes));
                        builder.Append("(");

                        for (int i = 0; i < sequence.Children.Count; i++)
                        {
                            var note = (Note)sequence.Children[i];
                            builder.Append("(");
                            Emit(builder, note.Expression, errors);
                            builder.Append(", ");
                            builder.Append(note.Duration);
                            builder.Append("f)");

                            if (i < sequence.Children.Count - 1)
                            {
                                builder.Append(", ");
                            }
                        }
                        builder.Append(")");
                    }
                    break;
                case NodeType.NoteSequenceVariation:
                    {
                        var variation = (NoteSequenceVariation)node;

                        builder.Append(nameof(seq));
                        builder.Append(".");
                        builder.Append(nameof(seq.from_n_notes));
                        builder.Append("(");

                        for (int i = 0; i < variation.Expressions.Count; i++)
                        {
                            Emit(builder, variation.Expressions[i], errors);
                            if (i < variation.Expressions.Count - 1)
                            {
                                builder.Append(", ");
                            }
                        }
                        builder.Append(")");
                    }
                    break;
                case NodeType.Operator:
                    {
                        var op = (Operator)node;
                        builder.Append(op.Token);
                    }
                    break;
                case NodeType.Placeholder:
                    {
                        var placeholder = (Placeholder)node;
                        var parent = node.Parent;

                        while (parent.NodeType == NodeType.Expression)
                        {
                            parent = parent.Parent;
                        }

                        if (parent.NodeType == NodeType.NoteSequenceVariation)
                        {
                            var variation = (NoteSequenceVariation)parent;

                            if (placeholder.Duration >= 0)
                            {
                                builder.Append(nameof(api.__nd));
                                builder.Append("(");
                            }
                            if (placeholder.PitchExpression != null)
                            {
                                builder.Append(nameof(api.__np));
                                builder.Append("(");
                            }

                            builder.Append(variation.SourceIdentifier);
                            builder.Append('[');

                            var index = -1;
                            for (int i = 0; i < variation.Expressions.Count; i++)
                            {
                                if (variation.Expressions[i].Children.Contains(node))
                                {
                                    index = i;
                                    break;
                                }
                            }

                            builder.Append(index);
                            builder.Append(']');

                            if (placeholder.PitchExpression != null)
                            {
                                builder.Append(", ");
                                Emit(builder, placeholder.PitchExpression, errors);
                                builder.Append(")");
                            }
                            if (placeholder.Duration >= 0)
                            {
                                builder.Append(", ");
                                builder.Append(placeholder.Duration);
                                builder.Append("f)");
                            }
                        }
                        else
                        {
                            errors.Append(placeholder.Root.FileName);
                            errors.Append('(');
                            errors.Append(placeholder.StartToken.LineNumber);
                            errors.Append("): ");
                            errors.AppendLine("The '?' placeholder can only be used when creating variations of existing note sequences.");
                        }
                    }
                    break;
                case NodeType.Return:
                    {

                    }
                    break;
                case NodeType.Struct:
                    {

                    }
                    break;
            }
        }

        public static class Runtime
        {
            public static unsafe class api
            {
                public static int __tempo;
                public static int __signature_t;
                public static int __signature_b;

                public static scale __scale;

                // Using scale
                public static void __using(scale s)
                {
                    __scale = s;
                }

                // Change note pitch
                public static note __np(note note, int pitch)
                {
                    note.pitch = pitch;
                    return note;
                }

                // Change note rhythm
                public static note __nd(note note, float duration)
                {
                    note.duration = duration;
                    return note;
                }

                public static void print<T>(T value)
                {
                    Console.WriteLine(value?.ToString() ?? "null");
                }

                public static void tempo(int value)
                {
                    __tempo = value;
                }

                public static int tempo()
                {
                    return __tempo;
                }

                public static void signature(int top, int bottom)
                {
                }
            }

            public struct note
            {
                public int pitch;
                public float duration;

                public static implicit operator note((int pitch, float rhythm) tuple)
                {
                    return new note { pitch = tuple.pitch, duration = tuple.rhythm };
                }

                public static note operator +(int offset, note n)
                {
                    n.pitch += offset;
                    return n;
                }

                public static note operator +(note n, int offset)
                {
                    n.pitch += offset;
                    return n;
                }

                public static note operator -(int offset, note n)
                {
                    n.pitch -= offset;
                    return n;
                }

                public static note operator -(note n, int offset)
                {
                    n.pitch -= offset;
                    return n;
                }

                public override string ToString()
                {
                    return string.Concat("{ pitch = ", pitch, ", duration = ", duration, " }");
                }
            }

            public struct scale
            {
                public int root;

                public static implicit operator scale(int i)
                {
                    return new scale { root = i };
                }

                public static implicit operator int(scale scale)
                {
                    return scale.root;
                }

                public static scale operator +(int offset, scale scale)
                {
                    scale.root += offset;
                    return scale;
                }

                public static scale operator +(scale scale, int offset)
                {
                    scale.root += offset;
                    return scale;
                }
            }

            public unsafe struct seq
            {
                public note* data;
                public int capacity;
                public int length;

                public note first_note => length > 0 ? data[0] : default;
                public note last_note => this[length - 1];

                public note this[int index]
                {
                    get { return data[index]; }
                }

                public void append(note note)
                {
                    if (length >= capacity)
                    {
                        var oldData = data;
                        var newData = ArenaAllocator.Allocate<note>(capacity <<= 1);

                        Unsafe.CopyBlock(newData, oldData, (uint)(sizeof(note) * length));

                        data = newData;
                    }

                    data[length] = note;
                    length++;
                }

                public void append(seq other)
                {
                    for (int i = 0; i < other.length; i++)
                    {
                        append(other[i] + __scale);
                    }
                }

                public static seq create(int capacity = 512)
                {
                    var result = new seq();
                    result.data = ArenaAllocator.Allocate<note>(capacity);
                    result.capacity = capacity;
                    result.length = 0;
                    return result;
                }

                public static implicit operator int(seq s)
                {
                    if (s.length > 1)
                    {
                        throw new InvalidOperationException("Can not do this");
                    }

                    return s[0].pitch;
                }

                public override string ToString()
                {
                    var builder = new StringBuilder();
                    for (int i = 0; i < length; i++)
                    {
                        builder.Append(this[i].ToString());
                        builder.Append("\n");
                    }
                    return builder.ToString();
                }

                public static seq from_n_notes(note n0)
                {
                    var result = create();
                    result.append(n0);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46, note n47)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    result.append(n47);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46, note n47, note n48)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    result.append(n47);
                    result.append(n48);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46, note n47, note n48, note n49)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    result.append(n47);
                    result.append(n48);
                    result.append(n49);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46, note n47, note n48, note n49, note n50)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    result.append(n47);
                    result.append(n48);
                    result.append(n49);
                    result.append(n50);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46, note n47, note n48, note n49, note n50, note n51)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    result.append(n47);
                    result.append(n48);
                    result.append(n49);
                    result.append(n50);
                    result.append(n51);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46, note n47, note n48, note n49, note n50, note n51, note n52)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    result.append(n47);
                    result.append(n48);
                    result.append(n49);
                    result.append(n50);
                    result.append(n51);
                    result.append(n52);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46, note n47, note n48, note n49, note n50, note n51, note n52, note n53)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    result.append(n47);
                    result.append(n48);
                    result.append(n49);
                    result.append(n50);
                    result.append(n51);
                    result.append(n52);
                    result.append(n53);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46, note n47, note n48, note n49, note n50, note n51, note n52, note n53, note n54)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    result.append(n47);
                    result.append(n48);
                    result.append(n49);
                    result.append(n50);
                    result.append(n51);
                    result.append(n52);
                    result.append(n53);
                    result.append(n54);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46, note n47, note n48, note n49, note n50, note n51, note n52, note n53, note n54, note n55)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    result.append(n47);
                    result.append(n48);
                    result.append(n49);
                    result.append(n50);
                    result.append(n51);
                    result.append(n52);
                    result.append(n53);
                    result.append(n54);
                    result.append(n55);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46, note n47, note n48, note n49, note n50, note n51, note n52, note n53, note n54, note n55, note n56)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    result.append(n47);
                    result.append(n48);
                    result.append(n49);
                    result.append(n50);
                    result.append(n51);
                    result.append(n52);
                    result.append(n53);
                    result.append(n54);
                    result.append(n55);
                    result.append(n56);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46, note n47, note n48, note n49, note n50, note n51, note n52, note n53, note n54, note n55, note n56, note n57)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    result.append(n47);
                    result.append(n48);
                    result.append(n49);
                    result.append(n50);
                    result.append(n51);
                    result.append(n52);
                    result.append(n53);
                    result.append(n54);
                    result.append(n55);
                    result.append(n56);
                    result.append(n57);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46, note n47, note n48, note n49, note n50, note n51, note n52, note n53, note n54, note n55, note n56, note n57, note n58)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    result.append(n47);
                    result.append(n48);
                    result.append(n49);
                    result.append(n50);
                    result.append(n51);
                    result.append(n52);
                    result.append(n53);
                    result.append(n54);
                    result.append(n55);
                    result.append(n56);
                    result.append(n57);
                    result.append(n58);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46, note n47, note n48, note n49, note n50, note n51, note n52, note n53, note n54, note n55, note n56, note n57, note n58, note n59)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    result.append(n47);
                    result.append(n48);
                    result.append(n49);
                    result.append(n50);
                    result.append(n51);
                    result.append(n52);
                    result.append(n53);
                    result.append(n54);
                    result.append(n55);
                    result.append(n56);
                    result.append(n57);
                    result.append(n58);
                    result.append(n59);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46, note n47, note n48, note n49, note n50, note n51, note n52, note n53, note n54, note n55, note n56, note n57, note n58, note n59, note n60)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    result.append(n47);
                    result.append(n48);
                    result.append(n49);
                    result.append(n50);
                    result.append(n51);
                    result.append(n52);
                    result.append(n53);
                    result.append(n54);
                    result.append(n55);
                    result.append(n56);
                    result.append(n57);
                    result.append(n58);
                    result.append(n59);
                    result.append(n60);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46, note n47, note n48, note n49, note n50, note n51, note n52, note n53, note n54, note n55, note n56, note n57, note n58, note n59, note n60, note n61)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    result.append(n47);
                    result.append(n48);
                    result.append(n49);
                    result.append(n50);
                    result.append(n51);
                    result.append(n52);
                    result.append(n53);
                    result.append(n54);
                    result.append(n55);
                    result.append(n56);
                    result.append(n57);
                    result.append(n58);
                    result.append(n59);
                    result.append(n60);
                    result.append(n61);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46, note n47, note n48, note n49, note n50, note n51, note n52, note n53, note n54, note n55, note n56, note n57, note n58, note n59, note n60, note n61, note n62)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    result.append(n47);
                    result.append(n48);
                    result.append(n49);
                    result.append(n50);
                    result.append(n51);
                    result.append(n52);
                    result.append(n53);
                    result.append(n54);
                    result.append(n55);
                    result.append(n56);
                    result.append(n57);
                    result.append(n58);
                    result.append(n59);
                    result.append(n60);
                    result.append(n61);
                    result.append(n62);
                    return result;
                }

                public static seq from_n_notes(note n0, note n1, note n2, note n3, note n4, note n5, note n6, note n7, note n8, note n9, note n10, note n11, note n12, note n13, note n14, note n15, note n16, note n17, note n18, note n19, note n20, note n21, note n22, note n23, note n24, note n25, note n26, note n27, note n28, note n29, note n30, note n31, note n32, note n33, note n34, note n35, note n36, note n37, note n38, note n39, note n40, note n41, note n42, note n43, note n44, note n45, note n46, note n47, note n48, note n49, note n50, note n51, note n52, note n53, note n54, note n55, note n56, note n57, note n58, note n59, note n60, note n61, note n62, note n63)
                {
                    var result = create();
                    result.append(n0);
                    result.append(n1);
                    result.append(n2);
                    result.append(n3);
                    result.append(n4);
                    result.append(n5);
                    result.append(n6);
                    result.append(n7);
                    result.append(n8);
                    result.append(n9);
                    result.append(n10);
                    result.append(n11);
                    result.append(n12);
                    result.append(n13);
                    result.append(n14);
                    result.append(n15);
                    result.append(n16);
                    result.append(n17);
                    result.append(n18);
                    result.append(n19);
                    result.append(n20);
                    result.append(n21);
                    result.append(n22);
                    result.append(n23);
                    result.append(n24);
                    result.append(n25);
                    result.append(n26);
                    result.append(n27);
                    result.append(n28);
                    result.append(n29);
                    result.append(n30);
                    result.append(n31);
                    result.append(n32);
                    result.append(n33);
                    result.append(n34);
                    result.append(n35);
                    result.append(n36);
                    result.append(n37);
                    result.append(n38);
                    result.append(n39);
                    result.append(n40);
                    result.append(n41);
                    result.append(n42);
                    result.append(n43);
                    result.append(n44);
                    result.append(n45);
                    result.append(n46);
                    result.append(n47);
                    result.append(n48);
                    result.append(n49);
                    result.append(n50);
                    result.append(n51);
                    result.append(n52);
                    result.append(n53);
                    result.append(n54);
                    result.append(n55);
                    result.append(n56);
                    result.append(n57);
                    result.append(n58);
                    result.append(n59);
                    result.append(n60);
                    result.append(n61);
                    result.append(n62);
                    result.append(n63);
                    return result;
                }
            }
        }
    }
}
