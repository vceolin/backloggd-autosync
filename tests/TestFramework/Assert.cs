using System;
using System.Collections;
using System.Collections.Generic;

namespace BackloggdAutoSync.Tests
{
    public class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }

    public static class Assert
    {
        public static void IsTrue(bool condition, string message = null)
        {
            if (!condition)
            {
                throw new AssertionException(message ?? "Expected true, but was false.");
            }
        }

        public static void IsFalse(bool condition, string message = null)
        {
            if (condition)
            {
                throw new AssertionException(message ?? "Expected false, but was true.");
            }
        }

        public static void AreEqual<T>(T expected, T actual, string message = null)
        {
            if (expected == null && actual == null) return;
            if (expected == null || !expected.Equals(actual))
            {
                string msg = string.Format("{0}Expected: <{1}>, Actual: <{2}>",
                    message != null ? (message + " ") : "",
                    expected == null ? "null" : expected.ToString(),
                    actual == null ? "null" : actual.ToString());
                throw new AssertionException(msg);
            }
        }

        public static void AreNotEqual<T>(T notExpected, T actual, string message = null)
        {
            if (notExpected == null && actual == null)
            {
                throw new AssertionException(message ?? "Expected values to differ, but both were null.");
            }
            if (notExpected != null && notExpected.Equals(actual))
            {
                throw new AssertionException(string.Format("{0}Expected value not to equal <{1}>.",
                    message != null ? (message + " ") : "", notExpected));
            }
        }

        public static void IsNull(object value, string message = null)
        {
            if (value != null)
            {
                throw new AssertionException(string.Format("{0}Expected null, but was <{1}>.",
                    message != null ? (message + " ") : "", value));
            }
        }

        public static void IsNotNull(object value, string message = null)
        {
            if (value == null)
            {
                throw new AssertionException(message ?? "Expected non-null value, but was null.");
            }
        }

        public static void Contains(string expectedSubstring, string actualString, string message = null)
        {
            if (actualString == null || actualString.IndexOf(expectedSubstring, StringComparison.Ordinal) < 0)
            {
                throw new AssertionException(string.Format("{0}Expected \"{1}\" to contain \"{2}\".",
                    message != null ? (message + " ") : "", actualString, expectedSubstring));
            }
        }

        public static TException Throws<TException>(Action action, string message = null) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException ex)
            {
                return ex;
            }
            catch (Exception ex)
            {
                throw new AssertionException(string.Format("{0}Expected exception of type {1}, but caught {2}: {3}",
                    message != null ? (message + " ") : "", typeof(TException).Name, ex.GetType().Name, ex.Message));
            }
            throw new AssertionException(string.Format("{0}Expected exception of type {1}, but none was thrown.",
                message != null ? (message + " ") : "", typeof(TException).Name));
        }
    }
}
