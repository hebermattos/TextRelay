using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Sms.Api.Controllers;
using Sms.Application.Messages;
using Sms.Application.Providers;
using Sms.Domain.Messages;
using Sms.Infrastructure.Providers;

namespace Sms.Infrastructure.Tests;

public sealed class TwilioWebhooksControllerTests
{
    [Fact]
    public async Task Inbound_PersistsValidatedMessage()
    {
        var tenantId=Guid.NewGuid(); var messages=new MessageRepository();
        var controller=Create(tenantId,messages,Form(("AccountSid","AC1"),("MessageSid","SM1"),("From","+15551234567"),("To","+15557654321"),("Body","hello")));
        var result=await controller.Inbound(default);
        Assert.IsType<ContentResult>(result);
        Assert.NotNull(messages.Inbound);
        Assert.Equal(tenantId,messages.Inbound!.TenantId);
        Assert.Equal(SmsStatus.Received,messages.Inbound.Status);
    }

    [Fact]
    public async Task Inbound_RejectsMissingRequiredMessageFields()
    {
        var controller=Create(Guid.NewGuid(),new MessageRepository(),Form(("AccountSid","AC1"),("From","+15551234567"),("To","+15557654321")));
        Assert.IsType<BadRequestResult>(await controller.Inbound(default));
    }

    [Fact]
    public async Task Inbound_ForbidsInvalidSignature()
    {
        var controller=Create(Guid.NewGuid(),new MessageRepository(),Form(("AccountSid","AC1"),("MessageSid","SM1")),validSignature:false);
        Assert.IsType<ForbidResult>(await controller.Inbound(default));
    }

    [Theory]
    [InlineData("sent",SmsStatus.Sent)]
    [InlineData("delivered",SmsStatus.Delivered)]
    [InlineData("failed",SmsStatus.Failed)]
    [InlineData("undelivered",SmsStatus.Failed)]
    public async Task Status_MapsProviderStatus(string value,SmsStatus expected)
    {
        var messages=new MessageRepository();
        var controller=Create(Guid.NewGuid(),messages,Form(("AccountSid","AC1"),("MessageSid","SM1"),("MessageStatus",value),("From","+15557654321")));
        Assert.IsType<NoContentResult>(await controller.Status(default));
        Assert.Equal(expected,messages.Status);
    }

    [Fact]
    public async Task Status_RejectsUnknownProviderStatus()
    {
        var controller=Create(Guid.NewGuid(),new MessageRepository(),Form(("AccountSid","AC1"),("MessageSid","SM1"),("MessageStatus","unknown"),("From","+15557654321")));
        Assert.IsType<BadRequestResult>(await controller.Status(default));
    }

    [Fact]
    public async Task Status_RejectsMissingMessageSid()
    {
        var controller=Create(Guid.NewGuid(),new MessageRepository(),Form(("AccountSid","AC1"),("MessageStatus","sent"),("From","+15557654321")));
        Assert.IsType<BadRequestResult>(await controller.Status(default));
    }

    [Fact]
    public async Task Status_ForbidsUnknownAccount()
    {
        var form=Form(("AccountSid","AC1"),("MessageSid","SM1"),("From","+15557654321"));
        var controller=Create(Guid.NewGuid(),new MessageRepository(),form,configurationExists:false);
        Assert.IsType<ForbidResult>(await controller.Status(default));
    }

    [Fact]
    public async Task Status_ForbidsMissingTenantRouteNumber()
    {
        var controller=Create(Guid.NewGuid(),new MessageRepository(),Form(("AccountSid","AC1"),("MessageSid","SM1"),("MessageStatus","sent")));
        Assert.IsType<ForbidResult>(await controller.Status(default));
    }

    private static TwilioWebhooksController Create(Guid tenantId,MessageRepository messages,Dictionary<string,string> values,bool validSignature=true,bool configurationExists=true)
    {
        const string token="auth-token";
        var config=configurationExists ? new TenantSmsProviderConfiguration(tenantId,"Twilio","AC1",token,"+15557654321",true,true) : null;
        var repository=new ProviderRepository(config);
        var webhookUrls=new ConfiguredSmsWebhookUrlProvider(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string,string?> { ["Sms:PublicBaseUrl"]="https://sms.example.com" }).Build());
        var controller=new TwilioWebhooksController(repository,messages,new TwilioWebhookValidator(),webhookUrls,new(new TestOptOutRepository()));
        var context=new DefaultHttpContext();
        context.Request.Scheme="https"; context.Request.Host=new HostString("sms.example.com"); context.Request.Path="/api/v1/webhooks/twilio/inbound";
        context.Request.ContentType="application/x-www-form-urlencoded";
        var encoded=string.Join("&",values.Select(x=>$"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        context.Request.Body=new MemoryStream(Encoding.UTF8.GetBytes(encoded));
        if(validSignature) context.Request.Headers["X-Twilio-Signature"]=Sign("https://sms.example.com/api/v1/webhooks/twilio/inbound",values,token);
        controller.ControllerContext=new ControllerContext { HttpContext=context };
        return controller;
    }

    private static Dictionary<string,string> Form(params (string Key,string Value)[] values)=>values.ToDictionary(x=>x.Key,x=>x.Value);

    private static string Sign(string url,Dictionary<string,string> values,string token)
    {
        var data=new StringBuilder(url);
        foreach(var item in values.OrderBy(x=>x.Key,StringComparer.Ordinal)) data.Append(item.Key).Append(item.Value);
        using var hmac=new HMACSHA1(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(data.ToString())));
    }

    private sealed class ProviderRepository(TenantSmsProviderConfiguration? config):ITenantSmsProviderRepository
    {
        public Task<TenantSmsProviderConfiguration?> GetAsync(Guid t,string p,CancellationToken c=default)=>Task.FromResult(config);
        public Task<TenantSmsProviderConfiguration?> GetDefaultAsync(Guid t,CancellationToken c=default)=>Task.FromResult(config);
        public Task<TenantSmsProviderConfiguration?> GetByAccountAndNumberAsync(string p,string a,string n,CancellationToken c=default)=>Task.FromResult(n=="+15557654321"?config:null);
        public Task UpsertAsync(TenantSmsProviderConfiguration x,CancellationToken c=default)=>Task.CompletedTask;
    }
    private sealed class MessageRepository:ISmsMessageRepository
    {
        public SmsMessage? Inbound{get;private set;} public SmsStatus? Status{get;private set;}
        public Task InsertInboundIfNotExistsAsync(SmsMessage m,CancellationToken c=default){Inbound=m;return Task.CompletedTask;}
        public Task UpdateStatusByProviderMessageIdAsync(Guid t,string p,string id,SmsStatus s,DateTimeOffset u,CancellationToken c=default){Status=s;return Task.CompletedTask;}
        public Task<SmsMessage?> GetByIdAsync(Guid t,Guid i,CancellationToken c=default)=>Task.FromResult<SmsMessage?>(null);
        public Task<IReadOnlyList<SmsMessage>> GetHistoryAsync(Guid t,int s,int n,CancellationToken c=default)=>Task.FromResult<IReadOnlyList<SmsMessage>>([]);
        public Task<IReadOnlyList<SmsStatusHistory>> GetStatusHistoryAsync(Guid t,Guid i,CancellationToken c=default)=>Task.FromResult<IReadOnlyList<SmsStatusHistory>>([]);
        public Task InsertAsync(SmsMessage m,CancellationToken c=default)=>Task.CompletedTask;
        public Task<bool> TryQueueScheduledAsync(Guid t,Guid i,DateTimeOffset u,CancellationToken c=default)=>Task.FromResult(false);
        public Task<bool> TryClaimQueuedAsync(Guid t,Guid i,DateTimeOffset u,CancellationToken c=default)=>Task.FromResult(false);
        public Task UpdateQueueStatusAsync(Guid t,Guid i,SmsQueueStatus s,DateTimeOffset u,CancellationToken c=default)=>Task.CompletedTask;
        public Task UpdateStatusAsync(Guid t,Guid i,SmsStatus s,string? p,DateTimeOffset u,CancellationToken c=default)=>Task.CompletedTask;
    }
}
