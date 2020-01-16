using System;
using System.Collections;
using System.IO;
using NUnit.Framework;

namespace Laboratory.Tests
{
    internal class ParseTests : HostFixture
    {
        public ParseTests(IHost host) : base(host) { }

        private static IEnumerable GenerateParseTestData()
        {
            var files = new DirectoryInfo("L0-Syntax-Parsing").GetFiles("*.ele", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var nameWithoutExtension = Path.GetFileNameWithoutExtension(file.Name);
                var expectedToPass = nameWithoutExtension.EndsWith("-pass", StringComparison.OrdinalIgnoreCase) ? true
                    : nameWithoutExtension.EndsWith("-fail", StringComparison.OrdinalIgnoreCase) ? false
                    : (bool?) null;
                if (!expectedToPass.HasValue) continue;
                yield return new TestCaseData((FileInfo: file, ExpectedToPass: expectedToPass.Value)).SetName(file.FullName.Split("L0-Syntax-Parsing\\")[1]);
            }
        }

        [TestCaseSource(nameof(GenerateParseTestData))]
        public void ParseTest((FileInfo FileInfo, bool ExpectedToPass) info) =>
            Assert.AreEqual(info.ExpectedToPass, _host.Parse(new HostContext(), info.FileInfo));
    }
}