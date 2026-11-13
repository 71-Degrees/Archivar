/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Archivar.Core.Operations;

namespace Archivar.Core.Tests.Operations;

public class CanonicalHostRedirectTests
{
    private const string Public = "https://www.example.com/";

    private static string? Target(string? publicUrl, string host, string method = "GET", string path = "/", string query = "", bool ws = false) =>
        CanonicalHostRedirect.TargetFor(publicUrl, host, method, path, query, ws);

    [Theory]
    [InlineData("example.com", "/", "", "https://www.example.com/")]
    [InlineData("trivianight-v9u4ia.azurewebsites.net", "/play/ABC123", "?v=1", "https://www.example.com/play/ABC123?v=1")]
    [InlineData("EXAMPLE.com:443", "/quizzes", "", "https://www.example.com/quizzes")]
    [InlineData("staging.example.com", "/demo/abc", "", "https://www.example.com/demo/abc")]
    public void OtherHosts_AreSentToTheSamePageOnThePublicAddress(string host, string path, string query, string expected) =>
        Assert.Equal(expected, Target(Public, host, path: path, query: query));

    [Theory]
    [InlineData("www.example.com")]
    [InlineData("WWW.Example.COM")]
    [InlineData("www.example.com:443")]
    public void ThePublicHostItself_IsNeverRedirected(string host) => Assert.Null(Target(Public, host));

    [Fact]
    public void NotConfigured_NeverRedirects()
    {
        Assert.Null(Target(null, "anything.azurewebsites.net"));
        Assert.Null(Target("", "anything.azurewebsites.net"));
        Assert.Null(Target("not a url", "anything.azurewebsites.net"));
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/version")]
    [InlineData("/api/admin/status")]
    [InlineData("/API/quizzes")]
    public void HealthChecksAndTheApi_AnswerOnAnyName(string path) => Assert.Null(Target(Public, "old.azurewebsites.net", path: path));

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public void OnlyGetAndHead_AreRedirected(string method) => Assert.Null(Target(Public, "old.azurewebsites.net", method: method));

    [Fact]
    public void Head_IsRedirectedLikeGet() => Assert.NotNull(Target(Public, "old.azurewebsites.net", method: "HEAD"));

    [Fact]
    public void LiveConnections_AreNeverRedirected() => Assert.Null(Target(Public, "old.azurewebsites.net", ws: true));

    [Theory]
    [InlineData("localhost:5199")]
    [InlineData("127.0.0.1:5199")]
    public void LocalDevelopment_IsNeverRedirected(string host) => Assert.Null(Target(Public, host));
}
