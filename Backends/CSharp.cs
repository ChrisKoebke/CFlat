using CFlat.Syntax;
using Microsoft.CSharp;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static CFlat.Backends.CSharp.Runtime;
using static CFlat.Backends.CSharp.Runtime.api;

namespace CFlat.Backends
{
    public class CSharp : IBackend
    {
        private static Dictionary<string, string> MethodReplacements = new Dictionary<string, string>
        {
            ["using"] = "__using",
            ["<<"] = "__seq.append"
        };

        private Assembly _assembly;
        private CodeDomProvider _compiler = new Microsoft.CodeDom.Providers.DotNetCompilerPlatform.CSharpCodeProvider();

        public void Compile(Ast ast)
        {
            var watch = Stopwatch.StartNew();
            var builder = new StringBuilder();
            Emit(builder, ast);

            var sourceCode = builder.ToString();
            watch.Stop();

            Console.WriteLine("OUTPUT: " + watch.Elapsed.TotalMilliseconds + "ms");

            var parameters = new CompilerParameters();

            parameters.ReferencedAssemblies.Add(Path.GetFullPath(typeof(Runtime).Assembly.Location));
            parameters.GenerateInMemory = true;

            var result = _compiler.CompileAssemblyFromSource(parameters, sourceCode);
            if (result.Errors.Count > 0)
            {
                Console.WriteLine(sourceCode);
                Console.WriteLine();

                foreach (CompilerError error in result.Errors)
                {
                    Console.WriteLine(error.Line + ":" + error.ErrorText);
                }
                return;
            }

            _assembly = result.CompiledAssembly;
        }

        public void Run()
        {
            if (_assembly == null) return;

            var type = _assembly.GetType("program");
            var instance = Activator.CreateInstance(type);
            var main = type.GetMethod("main");

            main.Invoke(instance, null);
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

        public void Emit(StringBuilder builder, INode node)
        {
            switch (node.NodeType)
            {
                default:
                    {
                        for (int i = 0; i < node.Children.Count; i++)
                        {
                            Emit(builder, node.Children[i]);
                        }
                    }
                    break;
                case NodeType.Ast:
                    {
                        builder.Append("using static CFlat.Backends.CSharp.Runtime;\n\n");
                        builder.Append("using static CFlat.Backends.CSharp.Runtime.api;\n\n");

                        builder.Append("public class program {\n");
                        for (int i = 0; i < node.Children.Count; i++)
                        {
                            Emit(builder, node.Children[i]);
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

                                    Emit(builder, right);
                                    builder.Append(op.Token);
                                    Emit(builder, left);
                                }
                                else if (stack.Count >= 1)
                                {
                                    var left = stack.Pop();
                                    builder.Append(op.Token);
                                    Emit(builder, left);
                                }
                            }
                            else
                            {
                                stack.Push(node.Children[i]);
                            }
                        }

                        while (stack.Count > 0)
                        {
                            Emit(builder, stack.Pop());
                        }
                    }
                    break;
                case NodeType.Include:
                    {

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
                        Emit(builder, assignment.Expression);

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
                        builder.Append("public ");
                        builder.Append(method.ReturnTypeName.IsEmpty ? "void" : method.ReturnTypeName.ToString());
                        builder.Append(" ");
                        builder.Append(method.MethodName);
                        builder.Append("(");

                        for (int i = 0; i < method.ParameterNames?.Count; i++)
                        {
                            builder.Append(method.ParameterTypeNames[i]);
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
                            builder.Append(nameof(seq));
                            builder.Append(" __seq = new ");
                            builder.Append(nameof(seq));
                            builder.Append("();\n");

                            _scope.Peek().Add("__seq");
                        }

                        for (int i = 0; i < method.Children.Count; i++)
                        {
                            Emit(builder, method.Children[i]);
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
                            Emit(builder, node.Children[i]);
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
                        builder.Append(nameof(seq.make));
                        builder.Append("(");

                        for (int i = 0; i < sequence.Children.Count; i++)
                        {
                            var note = (Note)sequence.Children[i];
                            builder.Append("(");
                            Emit(builder, note.Expression);
                            builder.Append(", ");
                            builder.Append(note.Rhythm);
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
                        builder.Append(nameof(seq.make));
                        builder.Append("(");

                        for (int i = 0; i < variation.Expressions.Count; i++)
                        {
                            Emit(builder, variation.Expressions[i]);
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

                            if (placeholder.rhythm >= 0)
                            {
                                builder.Append(nameof(api.__nr));
                                builder.Append("(");
                            }
                            if (placeholder.pitchExpression != null)
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

                            if (placeholder.pitchExpression != null)
                            {
                                builder.Append(", ");
                                Emit(builder, placeholder.pitchExpression);
                                builder.Append(")");
                            }
                            if (placeholder.rhythm >= 0)
                            {
                                builder.Append(", ");
                                builder.Append(placeholder.rhythm);
                                builder.Append("f)");
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException("You used ? in the wrong place.");
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
                public static note __nr(note note, float rhythm)
                {
                    note.rhythm = rhythm;
                    return note;
                }

                public static void* alloc(int size)
                {
                    return (void*)Marshal.AllocHGlobal(size);
                }

                public static T* alloc<T>(int count) where T : unmanaged
                {
                    return (T*)alloc(count * sizeof(T));
                }

                public static void free(void* pointer)
                {
                    Marshal.FreeHGlobal((IntPtr)pointer);
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
                public float rhythm;

                public static implicit operator note((int pitch, float rhythm) tuple)
                {
                    return new note { pitch = tuple.pitch, rhythm = tuple.rhythm };
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
                    return string.Concat("{ pitch = ", pitch, ", rhythm = ", rhythm, " }");
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

            public class seq
            {
                private List<note> _data = new List<note>(); // TODO: make this a large pointer

                public int length => _data.Count;

                public note first_note => length > 0 ? _data[0] : default;
                public note last_note => this[length - 1];

                public note this[int index]
                {
                    get { return _data[index]; }
                }

                public void append(seq other)
                {
                    for (int i = 0; i < other.length; i++)
                    {
                        _data.Add(other[i] + __scale);
                    }
                }

                public static seq make(params note[] data)
                {
                    return new seq { _data = new List<note>(data) };
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
            }
        }
    }
}
