using System.Net;
using Microsoft.Extensions.Caching.Distributed;
using Sms.Application.Common;
using Sms.Application.Providers;
using Sms.Infrastructure.Providers;
using Sms.Infrastructure.Caching;
using Sms.Domain.Messages;

namespace Sms.Infrastructure.Tests;

public sealed class BandwidthSmsProviderTests
{
    [Fact]
    public async Task SendAsync_UsesOAuthBearerAndApplication()
    {
        var tenant=Guid.NewGuid();
        var messaging=new RecordingHandler(HttpStatusCode.Accepted, """{"id":"m1"}""");
        var oauth=new RecordingHandler(HttpStatusCode.OK, """{"access_token":"token","expires_in":3600}""");
        var provider=Create(tenant,messaging,oauth,Config(tenant));
        var result=await provider.SendAsync("", "+15550000002", "hello");
        Assert.Equal("m1",result.ProviderMessageId);
        Assert.Equal(SmsStatus.Pending,result.Status);
        Assert.Equal("Bearer",messaging.Scheme);
        Assert.Contains("\"applicationId\":\"app-1\"",messaging.Body);
        Assert.Equal("Basic",oauth.Scheme);
        Assert.Contains("grant_type=client_credentials",oauth.Body);
    }

    [Fact]
    public async Task SendAsync_ReusesCachedOAuthToken()
    {
        var tenant=Guid.NewGuid();
        var messaging=new RecordingHandler(HttpStatusCode.Accepted, """{"id":"m1"}""");
        var oauth=new RecordingHandler(HttpStatusCode.OK, """{"access_token":"token","expires_in":3600}""");
        var provider=Create(tenant,messaging,oauth,Config(tenant));

        await provider.SendAsync("", "+15550000002", "first");
        await provider.SendAsync("", "+15550000003", "second");

        Assert.Equal(1,oauth.RequestCount);
        Assert.Equal(2,messaging.RequestCount);
    }

    [Fact]
    public async Task SendAsync_DoesNotReuseOAuthTokenWhenCacheIsDisabled()
    {
        var tenant=Guid.NewGuid();
        var messaging=new RecordingHandler(HttpStatusCode.Accepted, """{"id":"m1"}""");
        var oauth=new RecordingHandler(HttpStatusCode.OK, """{"access_token":"token","expires_in":3600}""");
        var provider=Create(tenant,messaging,oauth,Config(tenant),cacheEnabled:false);

        await provider.SendAsync("", "+15550000002", "first");
        await provider.SendAsync("", "+15550000003", "second");

        Assert.Equal(2,oauth.RequestCount);
        Assert.Equal(2,messaging.RequestCount);
    }

    [Fact]
    public async Task SendAsync_RejectsFromOverride()
    {
        var tenant=Guid.NewGuid();
        var provider=Create(tenant,new RecordingHandler(HttpStatusCode.Accepted,"{}"),new RecordingHandler(HttpStatusCode.OK,"{}"),Config(tenant));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>provider.SendAsync("+1999","+2","x"));
    }

    [Fact]
    public async Task SendAsync_RejectsMissingSettings()
    {
        var tenant=Guid.NewGuid();
        var config=new TenantSmsProviderConfiguration(tenant,"Bandwidth","client","secret","+1",true,true,null);
        var provider=Create(tenant,new RecordingHandler(HttpStatusCode.Accepted,"{}"),new RecordingHandler(HttpStatusCode.OK,"{}"),config);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>provider.SendAsync("","+2","x"));
    }

    private static BandwidthSmsProvider Create(
        Guid tenant,
        HttpMessageHandler messaging,
        HttpMessageHandler oauth,
        TenantSmsProviderConfiguration config,
        bool cacheEnabled = true)
    {
        var factory=new Factory(new HttpClient(oauth){BaseAddress=new Uri("https://api.bandwidth.com/")});
        return new BandwidthSmsProvider(
            new HttpClient(messaging){BaseAddress=new Uri("https://messaging.bandwidth.com/")},
            factory,
            new TenantContext(tenant),
            new Repo(config),
            cacheEnabled ? new TestDistributedCache() : new DisabledDistributedCacheProxy());
    }

    private static TenantSmsProviderConfiguration Config(Guid tenant)=>
        new(tenant,"Bandwidth","client","secret","+15550000001",true,true,"""{"accountId":"12345","applicationId":"app-1"}""");

    private sealed record TenantContext(Guid TenantId):ITenantContext;

    private sealed class Repo(TenantSmsProviderConfiguration c):ITenantSmsProviderRepository
    {
        public Task<TenantSmsProviderConfiguration?> GetAsync(Guid t,string p,CancellationToken x=default)=>Task.FromResult<TenantSmsProviderConfiguration?>(c);
        public Task<TenantSmsProviderConfiguration?> GetDefaultAsync(Guid t,CancellationToken x=default)=>Task.FromResult<TenantSmsProviderConfiguration?>(c);
        public Task<TenantSmsProviderConfiguration?> GetByAccountAndNumberAsync(string p,string a,string n,CancellationToken x=default)=>Task.FromResult<TenantSmsProviderConfiguration?>(c);
        public Task UpsertAsync(TenantSmsProviderConfiguration x,CancellationToken c=default)=>Task.CompletedTask;
    }

    private sealed class Factory(HttpClient client):IHttpClientFactory
    {
        public HttpClient CreateClient(string name)=>client;
    }

    private sealed class RecordingHandler(HttpStatusCode status,string response):HttpMessageHandler
    {
        public string Body{get;private set;}="";
        public string? Scheme{get;private set;}
        public int RequestCount{get;private set;}

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c)
        {
            RequestCount++;
            Body=r.Content is null?"":await r.Content.ReadAsStringAsync(c);
            Scheme=r.Headers.Authorization?.Scheme;
            return new HttpResponseMessage(status){Content=new StringContent(response)};
        }
    }

    private sealed class TestDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string,byte[]> values=new(StringComparer.Ordinal);

        public byte[]? Get(string key)=>values.GetValueOrDefault(key);
        public Task<byte[]?> GetAsync(string key,CancellationToken token=default)=>Task.FromResult(Get(key));
        public void Refresh(string key) { }
        public Task RefreshAsync(string key,CancellationToken token=default)=>Task.CompletedTask;
        public void Remove(string key)=>values.Remove(key);
        public Task RemoveAsync(string key,CancellationToken token=default){Remove(key);return Task.CompletedTask;}
        public void Set(string key,byte[] value,DistributedCacheEntryOptions options)=>values[key]=value;
        public Task SetAsync(string key,byte[] value,DistributedCacheEntryOptions options,CancellationToken token=default)
        {
            Set(key,value,options);
            return Task.CompletedTask;
        }
    }
}
