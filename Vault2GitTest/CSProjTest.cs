using System;
using System.IO;
using NUnit.Framework;
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
                Processor.RemoveSccFromCsProj(tempFile);
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
            const string FILE_PATH = @"data\testWithAmpersand.xml";
            Assert.DoesNotThrow(() => Processor.RemoveSccFromCsProj(FILE_PATH));
        }
    }
}