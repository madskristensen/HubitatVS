using System;
using System.Net;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HubitatVS.Tests
{
    [TestClass]
    public class HubitatHubClientHelpersTests
    {
        private static readonly Type ClientType = typeof(HubitatHubClient);

        [TestMethod]
        public void BuildBaseAddress_AddsHttpsAndNormalizesPath()
        {
            var uri = InvokePrivateStatic<Uri>("BuildBaseAddress", "example.local/hub");

            Assert.AreEqual("https", uri.Scheme);
            Assert.AreEqual("example.local", uri.Host);
            Assert.AreEqual("/", uri.AbsolutePath);
        }

        [TestMethod]
        public void BuildBaseAddress_InvalidHost_Throws()
        {
            Assert.ThrowsException<TargetInvocationException>(() =>
            {
                InvokePrivateStatic<Uri>("BuildBaseAddress", "::::");
            });
        }

        [TestMethod]
        public void NormalizeSource_NormalizesLineEndingsAndTrims()
        {
            var normalized = InvokePrivateStatic<string>("NormalizeSource", "\r\nline1\r\nline2\n\r\n");
            Assert.AreEqual("line1\nline2", normalized);
        }

        [TestMethod]
        public void IsCreateAccepted_RecognizesExpectedStatusCodes()
        {
            Assert.IsTrue(InvokePrivateStatic<bool>("IsCreateAccepted", HttpStatusCode.OK));
            Assert.IsTrue(InvokePrivateStatic<bool>("IsCreateAccepted", HttpStatusCode.Created));
            Assert.IsTrue(InvokePrivateStatic<bool>("IsCreateAccepted", HttpStatusCode.Found));
            Assert.IsFalse(InvokePrivateStatic<bool>("IsCreateAccepted", HttpStatusCode.BadRequest));
        }

        [TestMethod]
        public void TryParseCreatedCodeId_ParsesLocationAndBodyFallback()
        {
            var descriptor = CreateDescriptor(HubitatCodeKind.Driver, "/driver");

            var idFromLocation = InvokePrivateStatic<int?>(
                "TryParseCreatedCodeId",
                descriptor,
                "https://hub/driver/editor/42",
                string.Empty);

            var idFromBody = InvokePrivateStatic<int?>(
                "TryParseCreatedCodeId",
                descriptor,
                null,
                "var globalDriverIdToEdit = 314;");

            Assert.AreEqual(42, idFromLocation);
            Assert.AreEqual(314, idFromBody);
        }

        [TestMethod]
        public void FormatDiagnosticText_TruncatesLongText()
        {
            var longText = new string('a', 1305);
            var result = InvokePrivateStatic<string>("FormatDiagnosticText", longText);

            Assert.AreEqual(1201, result.Length);
            Assert.AreEqual('a', result[0]);
            Assert.AreNotEqual('a', result[result.Length - 1]);
        }

        private static object CreateDescriptor(HubitatCodeKind kind, string editorBasePath)
        {
            var type = ClientType.GetNestedType("HubitatCodeDescriptor", BindingFlags.NonPublic)!;
            return Activator.CreateInstance(
                type,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: new object[] { kind, "driver", "Driver", "/hub2/userDeviceTypes", editorBasePath },
                culture: null)!;
        }

        private static T InvokePrivateStatic<T>(string methodName, params object?[] args)
        {
            var method = ClientType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
            return (T)method.Invoke(null, args)!;
        }
    }
}
