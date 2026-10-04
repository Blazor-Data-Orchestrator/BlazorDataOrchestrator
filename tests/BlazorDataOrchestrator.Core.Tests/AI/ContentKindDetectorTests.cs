using BlazorDataOrchestrator.Core.Services.AI;
using Xunit;

namespace BlazorDataOrchestrator.Core.Tests.AI;

public class ContentKindDetectorTests
{
    [Theory]
    [InlineData("{ \"a\": 1 }", ContentKind.Json)]
    [InlineData("{\n  \"ConnectionStrings\": { \"x\": \"using System;\" }\n}", ContentKind.Json)]
    [InlineData("{\n  \"a\": 1,\n}", ContentKind.Json)]
    [InlineData("[1, 2]", ContentKind.Json)]
    [InlineData("<?xml version=\"1.0\"?><package />", ContentKind.Xml)]
    [InlineData("<package><metadata /></package>", ContentKind.Xml)]
    [InlineData("using System;\npublic class A {}", ContentKind.CSharp)]
    [InlineData("// NUGET: SendGrid, 9.29.3\nclass A {}", ContentKind.CSharp)]
    [InlineData("namespace Foo;\nclass A {}", ContentKind.CSharp)]
    [InlineData("public static class Job { }", ContentKind.CSharp)]
    [InlineData("# comment\nimport json\n\ndef execute_job():\n    pass", ContentKind.Python)]
    [InlineData("from typing import Optional\nx = 1", ContentKind.Python)]
    [InlineData("class JobLogger:\n    pass", ContentKind.Python)]
    [InlineData("requests==2.31.0\npandas==2.1.4", ContentKind.Unknown)]
    [InlineData("", ContentKind.Unknown)]
    [InlineData("Just some prose about the class keyword.", ContentKind.Unknown)]
    public void Detects(string content, ContentKind expected)
    {
        Assert.Equal(expected, ContentKindDetector.Detect(content));
    }
}
