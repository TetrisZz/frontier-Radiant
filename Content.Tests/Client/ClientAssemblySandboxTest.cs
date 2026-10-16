using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Moq;
using NUnit.Framework;
using Robust.Shared.ContentPack;
using Robust.Shared.Log;
using Robust.Shared.Utility;

namespace Content.Tests.Client;

[TestFixture]
public sealed class ClientAssemblySandboxTest
{
    [TestCase("Content.Client")]
    [TestCase("Content.Shared")]
    public void DownloadedAssembliesPassTheEngineSandbox(string assemblyName)
    {
        // Run the actual checker without starting graphics, game prototypes or a server.
        var resources = new Mock<IResourceManager>();
        resources.Setup(r => r.ContentFileRead(It.IsAny<ResPath>())).Throws<FileNotFoundException>();
        var log = new Mock<ISawmill>();
        var type = typeof(IResourceManager).Assembly.GetType("Robust.Shared.ContentPack.AssemblyTypeChecker", true)!;
        var checker = Activator.CreateInstance(type, new object[] { resources.Object, log.Object })!;
        var check = type.GetMethod("CheckAssembly", new[] { typeof(string) })!;
        var file = Path.Combine(TestContext.CurrentContext.TestDirectory, assemblyName + ".dll");
        var passed = (bool) check.Invoke(checker, new object[] { file })!;
        var errors = string.Join(Environment.NewLine, log.Invocations
            .Where(call => call.Method.Name == "Error")
            .SelectMany(call => call.Arguments.OfType<string>()));
        Assert.That(passed, Is.True, errors);
    }
}
