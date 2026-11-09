using System;
using System.Diagnostics;
using System.Reflection;

namespace BackloggdAutoSync.Tests
{
    [AttributeUsage(AttributeTargets.Method)]
    public class TestAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class TestFixtureAttribute : Attribute { }

    public static class TestRunner
    {
        public static int Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=================================================");
            Console.WriteLine("      BackloggdAutoSync Unit Test Runner         ");
            Console.WriteLine("=================================================");
            Console.ResetColor();

            var asm = Assembly.GetExecutingAssembly();
            int passed = 0;
            int failed = 0;
            var sw = Stopwatch.StartNew();

            Type[] types = asm.GetTypes();
            foreach (Type t in types)
            {
                if (!t.Name.EndsWith("Tests") && t.GetCustomAttributes(typeof(TestFixtureAttribute), false).Length == 0)
                {
                    continue;
                }

                object instance = null;
                try
                {
                    instance = Activator.CreateInstance(t);
                }
                catch
                {
                    continue;
                }

                Console.WriteLine("\nSuite: {0}", t.Name);

                MethodInfo[] methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                foreach (MethodInfo m in methods)
                {
                    bool isTest = m.GetCustomAttributes(typeof(TestAttribute), false).Length > 0 ||
                                  m.Name.StartsWith("Test_") ||
                                  m.Name.StartsWith("Should_") ||
                                  m.Name.EndsWith("_Test");
                    if (!isTest) continue;

                    try
                    {
                        m.Invoke(instance, null);
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write("  [PASS] ");
                        Console.ResetColor();
                        Console.WriteLine(m.Name);
                        passed++;
                    }
                    catch (TargetInvocationException ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write("  [FAIL] ");
                        Console.ResetColor();
                        Console.WriteLine("{0} -> {1}", m.Name, ex.InnerException != null ? ex.InnerException.Message : ex.Message);
                        failed++;
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write("  [FAIL] ");
                        Console.ResetColor();
                        Console.WriteLine("{0} -> {1}", m.Name, ex.Message);
                        failed++;
                    }
                }
            }

            sw.Stop();
            Console.WriteLine("\n-------------------------------------------------");
            if (failed == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("All tests passed! ({0} tests in {1} ms)", passed, sw.ElapsedMilliseconds);
                Console.ResetColor();
                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("TEST RUN FAILED: {0} failed, {1} passed ({2} total in {3} ms)",
                    failed, passed, passed + failed, sw.ElapsedMilliseconds);
                Console.ResetColor();
                return 1;
            }
        }
    }
}
