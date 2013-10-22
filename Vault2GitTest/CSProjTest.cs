using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using NUnit.Framework;
using System.IO;
using Vault2Git.Lib;

namespace Vault2GitTest
{
    [TestFixture]
    public class CSProjTest
    {
        [Test]
        [Ignore("files can be different in CRs, so better to check visually")]
        public void CheckSccRemoved()
        {
            string initialFile = @"data\initial.txt";
            string resultFile = @"data\result.txt";

            string tempFile = Path.GetTempFileName();

            try
            {
                File.Copy(initialFile, tempFile, true);
                Vault2Git.Lib.Processor.RemoveSccFromCsProj(tempFile);
                string result = File.ReadAllText(tempFile);
                string expectedResult = File.ReadAllText(resultFile);
                Console.WriteLine(result);
                Assert.AreEqual(expectedResult, result);
            }
            finally
            {
                File.Delete(tempFile);
            }
        }

        [Test]
        public void ReadingXmlDocumentWithAmpersandDoesNotThrowException()
        {
            const string FILE_PATH = "/data/XlnTelecom.csproj";
            Processor.RemoveSccFromCsProj(FILE_PATH);
        }
    }
}
