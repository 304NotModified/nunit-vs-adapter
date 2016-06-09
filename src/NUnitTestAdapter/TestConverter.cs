// ****************************************************************
// Copyright (c) 2011-2013 NUnit Software. All rights reserved.
// ****************************************************************

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using NUnit.Core;
using NUnit.VisualStudio.TestAdapter.Internal;
using NUnitTestResult = NUnit.Core.TestResult;
using VSTestResult = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult;

namespace NUnit.VisualStudio.TestAdapter
{
    public class TestConverter
    {
        private readonly TestLogger logger;
        private readonly Dictionary<string, TestCase> vsTestCaseMap;
        private readonly string sourceAssembly;

        #region Constructors

        public TestConverter(TestLogger logger, string sourceAssembly)
        {
            this.logger = logger;
            this.sourceAssembly = sourceAssembly;
            this.vsTestCaseMap = new Dictionary<string, TestCase>();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Converts an NUnit test into a TestCase for Visual Studio,
        /// using the best method available according to the exact
        /// type passed and caching results for efficiency.
        /// </summary>
        public TestCase ConvertTestCase(ITest test)
        {
            if (test.IsSuite)
                throw new ArgumentException("The argument must be a test case", "test");

            // Return cached value if we have one
            if (vsTestCaseMap.ContainsKey(test.TestName.UniqueName))
                return vsTestCaseMap[test.TestName.UniqueName];
           
            // Convert to VS TestCase and cache the result
            var testCase = MakeTestCaseFromNUnitTest(test);
            vsTestCaseMap.Add(test.TestName.UniqueName, testCase);
            return testCase;             
        }

        public TestCase GetCachedTestCase(string key)
        {
            if (vsTestCaseMap.ContainsKey(key))
                return vsTestCaseMap[key];

            logger.SendErrorMessage("Test " + key + " not found in cache");
            return null;
        }

        public VSTestResult ConvertTestResult(NUnitTestResult result)
        {
            TestCase ourCase = GetCachedTestCase(result.Test.TestName.UniqueName);
            if (ourCase == null) return null;

            VSTestResult ourResult = new VSTestResult(ourCase)
                {
                    DisplayName = ourCase.DisplayName,
                    Outcome = ResultStateToTestOutcome(result.ResultState),
                    Duration = TimeSpan.FromSeconds(result.Time)
                };

            // TODO: Remove this when NUnit provides a better duration
            if (ourResult.Duration == TimeSpan.Zero && (ourResult.Outcome == TestOutcome.Passed || ourResult.Outcome == TestOutcome.Failed))
                ourResult.Duration = TimeSpan.FromTicks(1);
            ourResult.ComputerName = Environment.MachineName;

            // TODO: Stuff we don't yet set
            //   StartTime   - not in NUnit result
            //   EndTime     - not in NUnit result
            //   Messages    - could we add messages other than the error message? Where would they appear?
            //   Attachments - don't exist in NUnit

            if (result.Message != null)
                ourResult.ErrorMessage = GetErrorMessage(result);

            if (!string.IsNullOrEmpty(result.StackTrace))
            {
                string stackTrace = StackTraceFilter.Filter(result.StackTrace);
                ourResult.ErrorStackTrace = stackTrace;
            }

            return ourResult;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Makes a TestCase from an NUnit test, adding
        /// navigation data if it can be found.
        /// </summary>
        private TestCase MakeTestCaseFromNUnitTest(ITest nunitTest)
        {
            //var testCase = MakeTestCaseFromTestName(nunitTest.TestName);
            var testCase = new TestCase(
                                     nunitTest.TestName.FullName,
                                     new Uri(NUnitTestExecutor.ExecutorUri),
                                     this.sourceAssembly)
            {
                DisplayName = nunitTest.TestName.Name,
                CodeFilePath = null,
                LineNumber = 0
            };

            var navData = GetNavigationData(nunitTest.ClassName, nunitTest.MethodName);
            if (navData != null)
            {
                testCase.CodeFilePath = navData.FileName;
                testCase.LineNumber = navData.MinLineNumber;
            }

            testCase.AddTraitsFromNUnitTest(nunitTest);

            return testCase;
        }

        // public for testing
        public DiaNavigationData GetNavigationData(string className, string methodName)
        {
            return null;

            //var navData = DiaSession.GetNavigationData(className, methodName);

            //if (navData != null && navData.FileName != null) return navData;

            //if (navData == null || navData.FileName == null)
            //    logger.SendWarningMessage(string.Format("No source data found for {0}.{1}", className, methodName));

            //return navData;
        }

        // Public for testing
        public static TestOutcome ResultStateToTestOutcome(ResultState resultState)
        {
            switch (resultState)
            {
                case ResultState.Cancelled:
                    return TestOutcome.None;
                case ResultState.Error:
                    return TestOutcome.Failed;
                case ResultState.Failure:
                    return TestOutcome.Failed;
                case ResultState.Ignored:
                    return TestOutcome.Skipped;
                case ResultState.Inconclusive:
                    return TestOutcome.None;
                case ResultState.NotRunnable:
                    return TestOutcome.Failed;
                case ResultState.Skipped:
                    return TestOutcome.Skipped;
                case ResultState.Success:
                    return TestOutcome.Passed;
            }

            return TestOutcome.None;
        }

        private string GetErrorMessage(NUnitTestResult result)
        {
            string message = result.Message;
            string NL = Environment.NewLine;

            // If we're running in the IDE, remove any caret line from the message
            // since it will be displayed using a variable font and won't make sense.
            if (message != null && RunningUnderIDE && (result.ResultState == ResultState.Failure || result.ResultState == ResultState.Inconclusive))
            {
                string pattern = NL + "  -*\\^" + NL;
                message = Regex.Replace(message, pattern, NL, RegexOptions.Multiline);
            }

            return message;
        }

        #endregion

        #region Private Properties

        private string exeName;
        private bool RunningUnderIDE
        {
            get
            {
                if (exeName == null)
                {
                    Assembly entryAssembly = Assembly.GetEntryAssembly();
                    if (entryAssembly != null)
                        exeName = Path.GetFileName(AssemblyHelper.GetAssemblyPath(entryAssembly));
                }
                
                return exeName == "vstest.executionengine.exe";
            }
        }

        #endregion
    }
}