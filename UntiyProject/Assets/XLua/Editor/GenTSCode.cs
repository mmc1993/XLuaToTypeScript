using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Reflection;

namespace GenCode
{
    public class Test
    {
        public event System.Action Complate;
    }

    public static class GenTSCode
    {
        private class FuncInfo
        {
            public bool IsStatic = false;
            public string Name = "";
            public List<string>           OutParams = new();
            public List<(string, string)> InParams = new();
        }

        private class PropInfo
        {
            public bool IsStatic = false;
            public bool IsReadOnly = false;
            public string Name = "";
            public string Type = "";
            public string Value = "";
        }

        private class ClassInfo
        {
            public string Name = "";
            public bool IsEnum = false;
            public string ParentClass = "";
            public List<string> Interfaces = new();
            public List<FuncInfo> MemberFuncs = new();
            public List<PropInfo> MemberProps = new();
        }

        private class NamespaceInfo
        {
            public string Name = "";
            public List<ClassInfo>     Classs = new();
            public List<NamespaceInfo> Spaces = new();

            public ClassInfo FindClass(string name)
            {
                return Classs.Find(v => v.Name == name);
            }

            public NamespaceInfo FindSpace(string name)
            {
                return Spaces.Find(v => v.Name == name);
            }
        }

        private class Contex
        {
            public string FileName;
            public System.IO.TextWriter          FWriter;
            public Queue<System.Type>            GenTypes;
            public Dictionary<System.Type, bool> HasTypes;

            public NamespaceInfo GenTree = new();
        }

        private static Dictionary<string, string> OpRemap = new()
        {
            // +): __add
            // -): __sub
            // *): __mul
            // /): __div
            // %): __mod
            // ^): __pow
            // -): __unm
            // ==): __eq
            // <): __lt
            // <=): __le
            { ".ctor",              "constructor" },
            { "op_Addition",        "__add" },
            { "op_Subtraction",     "__sub" },
            { "op_Multiply",        "__mul" },
            { "op_Division",        "__div" },
            { "op_Modulus",         "__mod" },
            { "op_UnaryNegation",   "__unm" },
            { "op_Equality",        "__eq" },
            { "op_LessThan",        "__lt" },
            { "op_LessThanOrEqual", "__le" },
        };

        private static string S(string src)
        {
            return OpRemap.TryGetValue(src, out var dst) ? dst : src;
        }

        private static Dictionary<string, string> KWRemap = new()
        {
            { "System.Void", "void" },
            { "System.Single", "number" },
            { "System.Double", "number" },
            { "System.Int64", "number" },
            { "System.Int32", "number" },
            { "System.UInt32", "number" },
            { "System.UInt64", "number" },
            { "System.UInt16", "number" },
            { "System.String", "string" },
            { "System.Byte[]", "string" },
            { "System.Boolean", "boolean" },
        };

        private static string T(System.Type type)
        {
            //  System.Action
            if (type == typeof(System.Action))
            {
                return "() => void";
            }

            //  System.Action<T>
            //  System.Func<T>
            if (type.IsGenericType)
            {
                Debug.Assert(type.Name.StartsWith("Action")
                          || type.Name.StartsWith("Func"));
                var isFunc = type.Name.StartsWith("Func");
                var argments = type.GetGenericArguments();
                System.Text.StringBuilder sb = new();
                sb.Append("(");
                for (var i = isFunc ? 1 : 0; i != argments.Length; ++i)
                {
                    if (i > (isFunc ? 1 : 0)) { sb.Append(", "); }
                    sb.AppendFormat("_{0}: {1}", i, T(argments[i]));
                }
                sb.AppendFormat(") => {0}", isFunc ? T(argments[0]) : "void");
                return sb.ToString();
            }

            //  KWRemap
            {
                var keyword = type.Namespace + "." + type.Name;
                if (KWRemap.TryGetValue(keyword, out var v))
                {
                    return v;
                }
            }

            //  Other
            {
                var isArray = type.IsArray;
                if (isArray) { type = type.GetElementType(); }
                var keyword = type.Namespace + "." + type.Name;
                if (KWRemap.TryGetValue(keyword, out var v))
                {
                    keyword = v;
                }
                return keyword + (isArray ? "[]" : "");
            }
        }

        private static string Ident(int count)
        {
            System.Text.StringBuilder sb = new();
            for (var i = 0; i != count; ++i)
            {
                sb.Append("    ");
            }
            return sb.ToString();
        }

        [MenuItem("Test/GenTSCode")]
        public static void GenTest()
        {
            List<System.Type> types = new()
            {
                typeof(Test),
                typeof(Vector3),
            };
            Gen(types, "G:/TSDemo/types/t.d.ts");
        }

        public static void Gen(List<System.Type> gentypes, string filename)
        {
            Contex ctx = new()
            {
                FileName = filename,GenTypes = new(gentypes),
            };
            ctx.HasTypes = new();
            gentypes.ForEach(v => ctx.HasTypes.Add(v, true));

            BeginContex(ref ctx);
            ApplyContex(ctx);
            EndedContex(ctx);
        }

        private static void BeginContex(ref Contex ctx)
        {
            ctx.FWriter = new System.IO.StreamWriter(ctx.FileName, false);
        }

        private static void EndedContex(Contex ctx)
        {
            System.Text.StringBuilder outputBuffer = new();
            outputBuffer.Append("declare namespace CS");
            GenCode(ctx.GenTree, 0, outputBuffer);
            ctx.FWriter.Write(outputBuffer.ToString());

            ctx.FWriter.Dispose();
        }

        private static void ApplyContex(Contex ctx)
        {
            while (ctx.GenTypes.Count != 0)
            {
                var type = ctx.GenTypes.Dequeue();
                var spaceInfo = HandleNamespace(ctx, type);
                var classInfo = HandleClass(ctx, spaceInfo, type);
                if (classInfo != null && ctx.HasTypes[type])
                {
                    if (!classInfo.IsEnum)
                    {
                        HandlePropertys(ctx, classInfo, type.GetProperties());
                        HandleMethods(ctx, classInfo, type.GetConstructors());
                        HandleMethods(ctx, classInfo, type.GetMethods());
                    }
                    HandleEvents(ctx, classInfo, type.GetEvents());
                    HandleFields(ctx, classInfo, type.GetFields());
                }
            }
        }

        private static void GenCode(NamespaceInfo spaceNode, int ident, System.Text.StringBuilder outputBuffer)
        {
            if (spaceNode.Name != "")
            {
                outputBuffer.Append(Ident(ident));
                outputBuffer.Append("namespace ");
                outputBuffer.Append(spaceNode.Name);
            }
            outputBuffer.Append("\n");
            outputBuffer.Append(Ident(ident));
            outputBuffer.Append("{\n");

            foreach (var spaceInfo in spaceNode.Spaces)
            {
                GenCode(spaceInfo, ident + 1, outputBuffer);
            }

            foreach (var classInfo in spaceNode.Classs)
            {
                GenCode(classInfo, ident + 1, outputBuffer);
            }

            outputBuffer.Append(Ident(ident));
            outputBuffer.Append("}\n");
        }

        private static void GenCode(ClassInfo classInfo, int ident, System.Text.StringBuilder outputBuffer)
        {
            System.Text.StringBuilder sb = new();
            if (classInfo.IsEnum)
            {
                sb.AppendFormat("{0}const enum {1}\n{0}{{\n", Ident(ident), classInfo.Name);
            }
            else
            {
                sb.AppendFormat("{0}class {1}", Ident(ident), classInfo.Name);
                if (classInfo.ParentClass != "")
                {
                    sb.Append(" extends ");
                    sb.Append(classInfo.ParentClass);
                }
                if (classInfo.Interfaces.Count != 0)
                {
                    sb.Append(" implements ");
                    sb.AppendJoin(", ", classInfo.Interfaces);
                }
                sb.Append("\n");
                sb.Append(Ident(ident));
                sb.Append("{\n");
            }

            //  func
            foreach (var funcInfo in classInfo.MemberFuncs)
            {
                sb.Append(Ident(ident + 1));
                if (funcInfo.IsStatic) { sb.Append("static "); }
                sb.Append(funcInfo.Name);
                sb.Append("(");
                for (var i = 0; i != funcInfo.InParams.Count; ++i)
                {
                    if (i != 0) { sb.Append(", "); }
                    sb.Append(funcInfo.InParams[i].Item2);
                    sb.Append(": ");
                    sb.Append(funcInfo.InParams[i].Item1);
                }
                sb.Append(")");

                if      (funcInfo.OutParams.Count == 1)
                {
                    sb.AppendFormat(": {0}", funcInfo.OutParams[0]);
                }
                else if (funcInfo.OutParams.Count >  1)
                {
                    sb.AppendFormat(": [{0}]", string.Join(", ", funcInfo.OutParams));
                }

                sb.Append(";\n");
            }

            //  prop
            foreach (var propInfo in classInfo.MemberProps)
            {
                sb.Append(Ident(ident + 1));
                if (classInfo.IsEnum)
                {
                    sb.Append(propInfo.Name);
                    sb.Append(" = ");
                    sb.Append(propInfo.Value);
                    sb.Append(",\n");
                }
                else
                {
                    if (propInfo.IsStatic)   { sb.Append("static ");   }
                    if (propInfo.IsReadOnly) { sb.Append("readonly "); }
                    sb.Append(propInfo.Name);
                    sb.Append(" : ");
                    sb.Append(propInfo.Type);
                    sb.Append(";\n");
                }
            }
            sb.Append(Ident(ident));
            sb.Append("}\n");
            outputBuffer.Append(sb.ToString());
        }

        private static NamespaceInfo HandleNamespace(Contex ctx, System.Type typeInfo)
        {
            var treeNode = ctx.GenTree;
            foreach (var name in typeInfo.Namespace.Split("."))
            {
                var nextNode = treeNode.FindSpace(name);
                if (nextNode == null)
                {
                    nextNode = new(){ Name=name };
                    treeNode.Spaces.Add(nextNode);
                }
                treeNode = nextNode;
            }
            return treeNode;
        }

        private static ClassInfo HandleClass(Contex ctx, NamespaceInfo space, System.Type type)
        {
            if (type.IsGenericType) { return null; }
            var classInfo = space.FindClass(type.Name);
            if (classInfo == null)
            {
                classInfo = new()
                {
                    Name = type.Name, IsEnum = type.IsEnum
                };

                if (type.BaseType != null)
                {
                    classInfo.ParentClass = T(type.BaseType);
                    PushType(ctx, type.BaseType);
                }

                foreach (var typeitem in type.GetInterfaces())
                {
                    if (typeitem.IsGenericType){continue;}
                    classInfo.Interfaces.Add(T(typeitem));
                    PushType(ctx, typeitem);
                }

                space.Classs.Add(classInfo);
            }
            return classInfo;
        }

        private static void HandleMethods(Contex ctx, ClassInfo classInfo, MethodBase[] methodBases)
        {
            foreach (var methodBase in methodBases)
            {
                if (methodBase is MethodInfo && !CheckMethodValid(methodBase as MethodInfo))
                {
                    continue;
                }

                var funcInfo = new FuncInfo()
                {
                    IsStatic = methodBase.IsStatic, Name = S(methodBase.Name),
                };

                if (methodBase is MethodInfo)
                {
                    funcInfo.OutParams.Add(T((methodBase as MethodInfo).ReturnType));
                    PushType(ctx, GetRawType((methodBase as MethodInfo).ReturnType));
                }

                foreach (var param in methodBase.GetParameters())
                {
                    if (ParamWrap(param) == "Ref" || ParamWrap(param) == "Nil")
                    {
                        funcInfo.InParams.Add((T(GetRawType(param.ParameterType)), param.Name));
                    }
                    if (ParamWrap(param) == "Out" || ParamWrap(param) == "Ref")
                    {
                        funcInfo.OutParams.Add(T(GetRawType(param.ParameterType)));
                    }
                    if (!param.ParameterType.IsGenericType)
                    {
                        PushType(ctx, GetRawType(param.ParameterType));
                    }
                }

                if (funcInfo.OutParams.Count > 1 && funcInfo.OutParams[0] == "void")
                {
                    funcInfo.OutParams.RemoveAt(0);
                }

                classInfo.MemberFuncs.Add(funcInfo);
            }
        }

        private static void HandlePropertys(Contex ctx, ClassInfo classInfo, PropertyInfo[] propertyInfos)
        {
            foreach (var propertyInfo in propertyInfos)
            {
                var propInfo = new PropInfo()
                {
                    IsReadOnly = propertyInfo.CanRead,
                    IsStatic = (propertyInfo.SetMethod != null? propertyInfo.SetMethod.IsStatic: false)
                            || (propertyInfo.GetMethod != null? propertyInfo.GetMethod.IsStatic: false),
                    Name = propertyInfo.Name, Type = T(propertyInfo.PropertyType),
                };
                classInfo.MemberProps.Add(propInfo);
                PushType(ctx, propertyInfo.PropertyType);
            }
        }

        private static void HandleEvents(Contex ctx, ClassInfo classInfo, EventInfo[] eventInfos)
        {
            foreach (var eventInfo in eventInfos)
            {
                var funcInfoAdd = new FuncInfo() { IsStatic = false, Name = eventInfo.Name };
                var funcInfoDel = new FuncInfo() { IsStatic = false, Name = eventInfo.Name };
                funcInfoAdd.InParams.Add(("\"+\"", "event"));
                funcInfoDel.InParams.Add(("\"-\"", "event"));
                funcInfoAdd.InParams.Add((T(GetRawType(eventInfo.EventHandlerType)), "call"));
                funcInfoDel.InParams.Add((T(GetRawType(eventInfo.EventHandlerType)), "call"));
                funcInfoAdd.OutParams.Add("void");
                funcInfoDel.OutParams.Add("void");
                classInfo.MemberFuncs.Add(funcInfoAdd);
                classInfo.MemberFuncs.Add(funcInfoDel);
                PushType(ctx, eventInfo.EventHandlerType);
            }
        }

        private static void HandleFields(Contex ctx, ClassInfo classInfo, FieldInfo[] fieldInfos)
        {
            foreach (var fieldInfo in fieldInfos)
            {
                if (!fieldInfo.IsPublic || (classInfo.IsEnum && fieldInfo.IsSpecialName)) { continue; }

                var propInfo = new PropInfo()
                {
                    IsReadOnly = fieldInfo.IsLiteral || fieldInfo.IsInitOnly,
                    IsStatic = fieldInfo.IsStatic,
                    Type = T(fieldInfo.FieldType),
                    Name = fieldInfo.Name, Value = "",
                };
                if (classInfo.IsEnum)
                {
                    propInfo.Value = fieldInfo.GetRawConstantValue().ToString();
                }
                classInfo.MemberProps.Add(propInfo);
                PushType(ctx, fieldInfo.FieldType);
            }
        }

        private static void PushType(Contex ctx, System.Type type)
        {
            if (type.IsArray) { type = type.GetElementType(); }

            if (ctx.HasTypes.ContainsKey(type)      ||
                type.IsPrimitive || type.IsPointer  ||
                KWRemap.ContainsKey(type.Namespace + "." + type.Name))
            {
                return;
            }
            ctx.HasTypes.Add(type, type.IsEnum); ctx.GenTypes.Enqueue(type);
        }

        private static bool CheckMethodValid(MethodInfo methodInfo)
        {
            if (!methodInfo.IsPublic || methodInfo.ContainsGenericParameters)
            {
                return false;
            }

            if (methodInfo.IsSpecialName)
            {
                if (!methodInfo.Name.StartsWith("op") && !methodInfo.IsConstructor)
                {
                    return false;
                }
            }

            if (methodInfo.ReturnType.ContainsGenericParameters ||
                methodInfo.ReturnType.IsGenericType             ||
                methodInfo.ReturnType.IsPointer                 ||
                methodInfo.ReturnType.IsInterface)
            {
                return false;
            }

            foreach (var paramInfo in methodInfo.GetParameters())
            {
                if (paramInfo.ParameterType.ContainsGenericParameters   ||
                    paramInfo.ParameterType.IsPointer                   ||
                    paramInfo.ParameterType.IsInterface) { return false; }

                if (paramInfo.ParameterType.IsGenericType && !paramInfo.ParameterType.Name.StartsWith("Action")
                                                          && !paramInfo.ParameterType.Name.StartsWith("Func"))
                {
                    return false;
                }
            }
            return true;
        }

        private static System.Type GetRawType(System.Type type)
        {
            return type.Name.EndsWith("&") ? type.GetElementType() : type;
        }

        private static string ParamWrap(ParameterInfo parameterInfo)
        {
            return parameterInfo.ParameterType.Name.EndsWith("&")
                 ? parameterInfo.Attributes == ParameterAttributes.Out
                 ? "Out" : "Ref" : "Nil";
        }
    }
}