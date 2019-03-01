﻿using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using NUnit.Framework.Constraints;

[TestFixture]
partial class Tests
{
    private static string projectPath = null;

    static string ProjectPath
    {
        get
        {
            return System.Threading.LazyInitializer.EnsureInitialized(ref projectPath, () =>
            {
                var path = Environment.CurrentDirectory;
                while (Exists(path) == false)
                    path = Path.GetDirectoryName(path);

                return path;

                bool Exists(string directoryPath)
                {
                    if (string.IsNullOrEmpty(directoryPath))
                        throw new ArgumentNullException(nameof(directoryPath));

                    var enumerator = Directory.EnumerateFiles(directoryPath, "*.csproj", SearchOption.TopDirectoryOnly).GetEnumerator();
                    try
                    {
                        return enumerator.MoveNext();
                    }
                    finally
                    {
                        enumerator.Dispose();
                    }
                }
            });
        }
    }

    static Internal.TestResult InvokeTest()
    {
        var currentTest = TestContext.CurrentContext?.Test;
        if (currentTest == null)
            throw new InvalidOperationException();

        var solution = new Solution();
        var method = solution.GetType().GetMethod(currentTest.MethodName);

        var parameters = method.GetParameters();
        var originalArguments = currentTest.Arguments;
        if (originalArguments.Length == 1 && originalArguments[0] is object[] nestedArguments)
            originalArguments = nestedArguments;

        var convertedArgs = new object[originalArguments.Length];
        for (var i = 0; i < convertedArgs.Length; i++)
        {
            var argument = originalArguments[i];
            if (parameters[i].ParameterType.IsInstanceOfType(argument) == false)
                argument = Internal.SmartConvert(argument, parameters[i].ParameterType);

            convertedArgs[i] = argument;
        }

        return new Internal.TestResult(method.Invoke(solution, convertedArgs));
    }

    private static class Internal
    {
        internal static object SmartConvert(object obj, Type targetType)
        {
            var s = obj.ToString();
            if (s.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var path = Path.Combine(ProjectPath, s);
                if (File.Exists(path))
                    s = File.ReadAllText(path);
            }
            else if (s.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                var path = Path.Combine(ProjectPath, s);
                if (File.Exists(path))
                {
                    var lines = File.ReadAllLines(path);

                    var elementType = targetType.GetElementType();
                    var array = Array.CreateInstance(elementType, lines.Length);
                    for (var i = 0; i < array.Length; i++)
                        array.SetValue(Convert.ChangeType(lines[i], elementType), i);
                    return array;
                }
            }

            return Newtonsoft.Json.JsonConvert.DeserializeObject(s, targetType);
        }

        internal static object Sorted(object obj)
        {
            if (obj is System.Collections.IEnumerable enumerable)
            {
                var list = new List<object>();
                var enumerator = enumerable.GetEnumerator();
                while (enumerator.MoveNext())
                    list.Add(Sorted(enumerator.Current));
                if (list.Count == 0)
                    return Array.Empty<object>();

                var elementType = list[0].GetType();
                var array = Array.CreateInstance(elementType, list.Count);
                for (var i = 0; i < array.Length; i++)
                    array.SetValue(list[i], i);

                if (elementType.IsArray)
                    Array.Sort(array, ArrayComparer.Default);
                else
                    Array.Sort(array);

                return array;
            }
            else
                return obj;
        }

        [Flags]
        internal enum ObjectComparison
        {
            None = 0,
            Unordered = (1 << 0),
        }

        internal sealed class TestResult : IEquatable<object>
        {
            private static readonly NUnitEqualityComparer comparer = new NUnitEqualityComparer() { CompareAsCollection = true };

            private readonly object obj;
            private readonly ObjectComparison comparison;

            public TestResult(object obj, ObjectComparison comparison = ObjectComparison.None)
            {
                this.obj = obj;
                this.comparison = comparison;
            }

            public TestResult IsUnordered() => new TestResult(obj, comparison | ObjectComparison.Unordered);

            public override bool Equals(object other)
            {
                var left = obj;
                var right = other;
                if (left is string == false && right is string)
                    right = SmartConvert(right, left.GetType());

                if (comparison.HasFlag(ObjectComparison.Unordered))
                {
                    left = Sorted(left);
                    right = Sorted(right);
                }

                var tolerance = Tolerance.Default;
                return comparer.AreEqual(left, right, ref tolerance);
            }

            public override int GetHashCode() => obj?.GetHashCode() ?? 0;
            public override string ToString() => obj?.ToString() ?? string.Empty;
        }

        private sealed class ArrayComparer : System.Collections.IComparer
        {
            public static readonly ArrayComparer Default = new ArrayComparer();
            public int Compare(object x, object y)
            {
                var a = (Array)x;
                var b = (Array)y;
                if (a.Length != b.Length)
                    return a.Length.CompareTo(b.Length);
                else
                {
                    for (var i = 0; i < a.Length; i++)
                    {
                        var result = 0;
                        if (a.GetValue(i) is IComparable ac)
                            result = ac.CompareTo(b.GetValue(i));
                        else if (b.GetValue(i) is IComparable bc)
                            result = bc.CompareTo(a.GetValue(i));

                        if (result != 0)
                            return result;
                    }

                    return 0;
                }
            }
        }
    }
}
