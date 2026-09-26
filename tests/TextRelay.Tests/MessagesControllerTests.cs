using Sms.Application.Auth;
using Moq;
using Microsoft.AspNetCore.Mvc;
using Sms.Api.Controllers;
using Sms.Application.Common;
using Sms.Application.Messages;
using Sms.Domain.Messages;

namespace Sms.Infrastructure.Tests;

public sealed class MessagesControllerTests
{
    [Fact]
    public async Task GetById_UsesTenantAndReturnsMessage()
    {
        var tenantId=Guid.NewGuid(); var id=Guid.NewGuid();
        var message=new SmsMessage { Id=id, TenantId=tenantId, To="+1", Body="x" };
        var repo=new Repository(message);
        var controller=Create(tenantId,repo);
        Assert.IsType<OkObjectResult>(await controller.GetById(id,default));
        Assert.Equal(tenantId,repo.LastTenant);
    }

    [Fact]
    public async Task GetById_WithMockedRepository_UsesAuthenticatedTenant()
    {
        var tenantId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var message = new SmsMessage { Id = messageId, TenantId = tenantId, To = "+1", Body = "test" };
        var repository = new Mock<ISmsMessageRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetByIdAsync(tenantId, messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message);

        var result = await Create(tenantId, repository.Object).GetById(messageId, default);

        Assert.IsType<OkObjectResult>(result);
        repository.Verify(x => x.GetByIdAsync(tenantId, messageId, It.IsAny<CancellationToken>()), Times.Once);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetHistory_WithMockedRepository_PassesTenantAndPagination()
    {
        var tenantId = Guid.NewGuid();
        var repository = new Mock<ISmsMessageRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetHistoryAsync(tenantId, 20, 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SmsMessage>());

        var result = await Create(tenantId, repository.Object).GetHistory(20, 50);

        Assert.IsType<OkObjectResult>(result);
        repository.VerifyAll();
    }

    [Fact]
    public async Task GetStatusHistory_WithMockedRepository_DoesNotQueryHistoryWhenMessageIsOutsideTenant()
    {
        var tenantId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var repository = new Mock<ISmsMessageRepository>(MockBehavior.Strict);
        repository.Setup(x => x.GetByIdAsync(tenantId, messageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SmsMessage?)null);

        var result = await Create(tenantId, repository.Object).GetStatusHistory(messageId, default);

        Assert.IsType<NotFoundResult>(result);
        repository.Verify(x => x.GetByIdAsync(tenantId, messageId, It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(x => x.GetStatusHistoryAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound()
    {
        Assert.IsType<NotFoundResult>(await Create(Guid.NewGuid(),new Repository(null)).GetById(Guid.NewGuid(),default));
    }

    [Fact]
    public async Task GetStatusHistory_UsesAuthenticatedTenant()
    {
        var tenantId=Guid.NewGuid(); var id=Guid.NewGuid();
        var repo=new Repository(new SmsMessage { Id=id, TenantId=tenantId });

        Assert.IsType<OkObjectResult>(await Create(tenantId,repo).GetStatusHistory(id,default));
        Assert.Equal(tenantId,repo.LastTenant);
        Assert.Equal(id,repo.LastMessageId);
    }

    [Fact]
    public async Task GetStatusHistory_ReturnsNotFoundForMessageOutsideTenant()
    {
        var repo=new Repository(null);
        Assert.IsType<NotFoundResult>(await Create(Guid.NewGuid(),repo).GetStatusHistory(Guid.NewGuid(),default));
        Assert.Null(repo.LastMessageId);
    }

    [Fact]
    public async Task GetHistory_RejectsNegativeSkip()
    {
        Assert.IsType<BadRequestObjectResult>(await Create(Guid.NewGuid(),new Repository(null)).GetHistory(-1,50));
    }

    [Theory]
    [InlineData(0,1,1)]
    [InlineData(0,500,200)]
    [InlineData(0,0,1)]
    public async Task GetHistory_ClampsTake(int skip,int take,int expected)
    {
        var repo=new Repository(null);
        Assert.IsType<OkObjectResult>(await Create(Guid.NewGuid(),repo).GetHistory(skip,take));
        Assert.Equal(expected,repo.LastTake);
    }

    [Fact]
    public async Task Send_ConvertsScheduledTimeBackToTenantTimeZone()
    {
        var tenantId=Guid.NewGuid();
        var repo=new Repository(null);
        var zone=TimeZoneInfo.CreateCustomTimeZone("Tenant/MinusThree",TimeSpan.FromHours(-3),"Tenant","Tenant");
        var controller=Create(tenantId,repo,zone,new FixedTimeProvider(new DateTimeOffset(2026,1,1,12,0,0,TimeSpan.Zero)));

        var action=await controller.Send(new SendSmsRequest("+15551234567","hello",ScheduledAt:new DateTime(2026,1,1,10,0,0)),default);

        var accepted=Assert.IsType<AcceptedAtActionResult>(action);
        var result=Assert.IsType<SendSmsResult>(accepted.Value);
        Assert.Equal(nameof(SmsQueueStatus.Scheduled),result.Status);
        Assert.Equal(new DateTimeOffset(2026,1,1,10,0,0,TimeSpan.FromHours(-3)),result.ScheduledAt);
        Assert.Equal(new DateTimeOffset(2026,1,1,13,0,0,TimeSpan.Zero),repo.Inserted!.ScheduledAtUtc);
    }

    private static MessagesController Create(Guid tenantId, Repository repo, TimeZoneInfo? zone=null, TimeProvider? clock=null)
        => Create(tenantId, (ISmsMessageRepository)repo, zone, clock);

    private static MessagesController Create(Guid tenantId, ISmsMessageRepository repo, TimeZoneInfo? zone=null, TimeProvider? clock=null)
    {
        var context=new TenantContext(tenantId);
        var timeZones=new TimeZones(zone ?? TimeZoneInfo.Utc);
        var service=new SendSmsService(context,repo,new Resolver(),new Publisher(),new(new TestOptOutRepository()),new SendSmsValidator(context,timeZones,new FakeUsers()),clock ?? TimeProvider.System);
        return new MessagesController(context,repo,service,timeZones);
    }
    private sealed class FakeUsers : IPortalUserRepository
    {
        public Task<PortalUserAccount?> GetActiveByUsernameAsync(string username, string context, string? tenantCode, CancellationToken cancellationToken = default) => Task.FromResult<PortalUserAccount?>(null);
        public Task<PortalUserAccount?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<PortalUserAccount?>(null);
    }

    private sealed record TenantContext(Guid TenantId):ITenantContext;
    private sealed class Resolver:ISmsProviderResolver { public ISmsProvider Resolve(string? provider=null)=>new Provider(); }
    private sealed class Provider:ISmsProvider
    {
        public string Name=>"Twilio";
        public Task<ProviderSendResult> SendAsync(string from,string to,string body,CancellationToken cancellationToken=default)=>
            Task.FromResult(new ProviderSendResult("unused",SmsStatus.Sent));
    }
    private sealed class Publisher:ISmsSendEventPublisher { public Task PublishAsync(Guid tenantId,Guid messageId,CancellationToken cancellationToken=default)=>Task.CompletedTask; }
    private sealed record TimeZones(TimeZoneInfo Zone):ITenantTimeZoneProvider { public Task<TimeZoneInfo> GetAsync(Guid tenantId,CancellationToken cancellationToken=default)=>Task.FromResult(Zone); }
    private sealed class FixedTimeProvider(DateTimeOffset value):TimeProvider { public override DateTimeOffset GetUtcNow()=>value; }
    private sealed class Repository(SmsMessage? message):ISmsMessageRepository
    {
        public Guid LastTenant{get;private set;} public Guid? LastMessageId{get;private set;} public int LastTake{get;private set;} public SmsMessage? Inserted{get;private set;}
        public Task<SmsMessage?> GetByIdAsync(Guid tenantId,Guid id,CancellationToken cancellationToken=default){LastTenant=tenantId;return Task.FromResult(message);}
        public Task<IReadOnlyList<SmsMessage>> GetHistoryAsync(Guid tenantId,int skip,int take,CancellationToken cancellationToken=default){LastTenant=tenantId;LastTake=take;return Task.FromResult<IReadOnlyList<SmsMessage>>([]);}
        public Task<IReadOnlyList<SmsStatusHistory>> GetStatusHistoryAsync(Guid tenantId,Guid messageId,CancellationToken cancellationToken=default){LastTenant=tenantId;LastMessageId=messageId;return Task.FromResult<IReadOnlyList<SmsStatusHistory>>([]);}
        public Task InsertAsync(SmsMessage m,CancellationToken c=default){Inserted=m;return Task.CompletedTask;}
        public Task InsertInboundIfNotExistsAsync(SmsMessage m,CancellationToken c=default)=>Task.CompletedTask;
        public Task<bool> TryQueueScheduledAsync(Guid t,Guid i,DateTimeOffset u,CancellationToken c=default)=>Task.FromResult(false);

        public Task<bool> TryClaimQueuedAsync(Guid t, Guid i, DateTimeOffset u, CancellationToken c = default) => Task.FromResult(false);
        public Task UpdateQueueStatusAsync(Guid t,Guid i,SmsQueueStatus s,DateTimeOffset u,CancellationToken c=default)=>Task.CompletedTask;
        public Task UpdateStatusAsync(Guid t,Guid i,SmsStatus s,string? p,DateTimeOffset u,CancellationToken c=default)=>Task.CompletedTask;
        public Task UpdateStatusByProviderMessageIdAsync(Guid t,string p,string id,SmsStatus s,DateTimeOffset u,CancellationToken c=default)=>Task.CompletedTask;
    }
}
