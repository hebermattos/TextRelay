using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sms.Api.Auth;
using Sms.Api.Controllers;
using Sms.Application.Administration;
using Sms.Application.Auth;
using Sms.Application.Common;
using Sms.Application.Messages;
using Sms.Application.Templates;
using Sms.Infrastructure.Providers;

namespace Sms.Infrastructure.Tests;

public sealed class RecentFeatureTests
{
    [Fact]
    public void RetryOptions_ValidateDefaultsAndBounds()
    {
        new SmsRetryOptions().Validate();
        Assert.Throws<InvalidOperationException>(() => new SmsRetryOptions { MaxAttempts = -1 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new SmsRetryOptions { MaxAttempts = 11 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new SmsRetryOptions { InitialIntervalSeconds = 0 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new SmsRetryOptions { InitialIntervalSeconds = 86401 }.Validate());
        new SmsRetryOptions { MaxAttempts = 0, InitialIntervalSeconds = 1 }.Validate();
        new SmsRetryOptions { MaxAttempts = 10, InitialIntervalSeconds = 86400 }.Validate();
    }


    [Fact]
    public async Task MessageAssistantController_ReturnsResultsAndBadRequests()
    {
        var tenant = new Tenant();
        var assistant = new Assistant();
        var controller = new MessageAssistantController(assistant, tenant);
        Assert.IsType<OkObjectResult>(await controller.Improve(new("hello"), default));
        Assert.Equal(tenant.TenantId, assistant.TenantId);
        Assert.IsType<OkObjectResult>(await controller.Validate(new("hello"), default));
        assistant.Throw = true;
        Assert.IsType<BadRequestObjectResult>(await controller.Improve(new("bad"), default));
        Assert.IsType<BadRequestObjectResult>(await controller.Validate(new("bad"), default));
    }

    [Fact]
    public async Task TemplateController_ValidatesListAndCreateRequests()
    {
        var repository = new Templates();
        var controller = CreateTemplateController(repository);

        Assert.IsType<BadRequestObjectResult>(await controller.List(-1, 20));
        Assert.IsType<OkObjectResult>(await controller.List(0, 500));
        Assert.Equal(200, repository.Take);

        foreach (var request in new[]
        {
            new SaveTemplateRequest("", "body"),
            new SaveTemplateRequest(new string('n', 121), "body"),
            new SaveTemplateRequest("name", ""),
            new SaveTemplateRequest("name", new string('b', 4001))
        })
            Assert.IsType<BadRequestObjectResult>(await controller.Create(request, default));
    }

    [Fact]
    public async Task TemplateController_CreatesUpdatesRendersAndDeletesTemplate()
    {
        var controller = CreateTemplateController(new Templates());
        var missing = Guid.NewGuid();

        Assert.IsType<NotFoundResult>(await controller.Get(missing, default));
        var created = Assert.IsType<CreatedAtActionResult>(
            await controller.Create(new(" Welcome ", " Hi {{recipientName}} from {{tenantName}} "), default));
        var id = Assert.IsType<Guid>(created.RouteValues!["id"]);

        Assert.IsType<OkObjectResult>(await controller.Get(id, default));
        Assert.IsType<NotFoundResult>(await controller.Update(missing, new("name", "body"), default));
        Assert.IsType<BadRequestObjectResult>(await controller.Update(id, new("", "body"), default));
        Assert.IsType<OkObjectResult>(await controller.Update(id, new("Updated", "{{missing}}"), default));
        Assert.IsType<BadRequestObjectResult>(await controller.Render(id, new(new()), default));

        await controller.Update(id, new("Updated", "Hi {{recipientName}} at {{tenantName}}"), default);
        Assert.IsType<OkObjectResult>(await controller.Render(id, new(new(), "Ana", "+1555"), default));
        Assert.IsType<NotFoundResult>(await controller.Render(missing, new(new()), default));
        Assert.IsType<NoContentResult>(await controller.Delete(id, default));
        Assert.IsType<NotFoundResult>(await controller.Delete(id, default));
    }

    private static MessageTemplatesController CreateTemplateController(Templates repository) =>
        new(new Tenant(), repository, new Portal());

    [Fact]
    public async Task OllamaAssistant_ImprovesAndValidatesResponses()
    {
        var settings = new AiSettings();
        var handler = new OllamaHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://ollama/") };
        var assistant = new OllamaMessageAssistant(client, settings);

        handler.Response = """{"response":"  Improved text  "}""";
        var improved = await assistant.ImproveAsync(settings.TenantId, "hello");
        Assert.Equal("Improved text", improved.Message);
        Assert.True(improved.IsValid);
        Assert.Contains("Improve prompt", handler.RequestBody!);

        handler.Response = JsonSerializer.Serialize(new { response = """prefix {"isValid":false,"issues":["too long"]} suffix""" });
        var validation = await assistant.ValidateAsync(settings.TenantId, "hello");
        Assert.False(validation.IsValid);
        Assert.Equal("too long", Assert.Single(validation.Issues));
        Assert.Contains("Validate prompt", handler.RequestBody!);

        handler.Response = """{"response":"not json"}""";
        validation = await assistant.ValidateAsync(settings.TenantId, "hello");
        Assert.False(validation.IsValid);
        Assert.Single(validation.Issues);

        await Assert.ThrowsAsync<ArgumentException>(() => assistant.ImproveAsync(settings.TenantId, ""));
        await Assert.ThrowsAsync<ArgumentException>(() => assistant.ImproveAsync(settings.TenantId, new string('x', 4001)));
    }

    [Fact]
    public async Task OllamaAssistant_CoversValidationEdgeCases()
    {
        var settings = new AiSettings();
        var handler = new OllamaHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://ollama/") };
        var assistant = new OllamaMessageAssistant(client, settings);

        handler.Response = JsonSerializer.Serialize(new { response = "{\"isValid\":true,\"issues\":null}" });
        var valid = await assistant.ValidateAsync(settings.TenantId, "hello");
        Assert.True(valid.IsValid);
        Assert.Empty(valid.Issues);

        handler.Response = JsonSerializer.Serialize(new { response = "{}" });
        var missingFields = await assistant.ValidateAsync(settings.TenantId, "hello");
        Assert.False(missingFields.IsValid);
        Assert.Empty(missingFields.Issues);

        handler.Response = JsonSerializer.Serialize(new { response = "no braces" });
        var invalid = await assistant.ValidateAsync(settings.TenantId, "hello");
        Assert.False(invalid.IsValid);
        Assert.Single(invalid.Issues);

        await Assert.ThrowsAsync<ArgumentException>(() => assistant.ValidateAsync(settings.TenantId, "   "));
        await Assert.ThrowsAsync<ArgumentException>(() => assistant.ValidateAsync(settings.TenantId, new string('x', 4001)));
    }

    [Fact]
    public async Task OllamaAssistant_PropagatesHttpAndEmptyResponseFailures()
    {
        var handler = new OllamaHandler { StatusCode = HttpStatusCode.ServiceUnavailable };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://ollama/") };
        var assistant = new OllamaMessageAssistant(client, new AiSettings());
        await Assert.ThrowsAsync<HttpRequestException>(() => assistant.ImproveAsync(Guid.NewGuid(), "hello"));

        handler.StatusCode = HttpStatusCode.OK;
        handler.Response = "null";
        await Assert.ThrowsAsync<InvalidOperationException>(() => assistant.ImproveAsync(Guid.NewGuid(), "hello"));
    }

    private sealed class Tenant : ITenantContext { public Guid TenantId { get; } = Guid.NewGuid(); }

    private sealed class Assistant : IMessageAssistant
    {
        public Guid TenantId { get; private set; }
        public bool Throw { get; set; }
        public Task<MessageAssistantResult> ImproveAsync(Guid tenantId, string message, CancellationToken cancellationToken = default)
        { TenantId = tenantId; if (Throw) throw new ArgumentException("bad"); return Task.FromResult(new MessageAssistantResult("improved", [], true)); }
        public Task<MessageAssistantResult> ValidateAsync(Guid tenantId, string message, CancellationToken cancellationToken = default)
        { TenantId = tenantId; if (Throw) throw new ArgumentException("bad"); return Task.FromResult(new MessageAssistantResult(message, [], true)); }
    }

    private sealed class Templates : IMessageTemplateRepository
    {
        private readonly Dictionary<Guid, MessageTemplate> items = new();
        public int Take { get; private set; }
        public Task<IReadOnlyList<MessageTemplate>> ListAsync(Guid tenantId, int skip, int take, CancellationToken cancellationToken = default)
        { Take = take; return Task.FromResult<IReadOnlyList<MessageTemplate>>(items.Values.Skip(skip).Take(take).ToArray()); }
        public Task<MessageTemplate?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(items.GetValueOrDefault(id));
        public Task<MessageTemplate> CreateAsync(Guid tenantId, string name, string body, CancellationToken cancellationToken = default)
        { var item = new MessageTemplate(Guid.NewGuid(), name, body, DateTimeOffset.UtcNow, null); items[item.Id] = item; return Task.FromResult(item); }
        public Task<MessageTemplate?> UpdateAsync(Guid tenantId, Guid id, string name, string body, CancellationToken cancellationToken = default)
        { if (!items.TryGetValue(id, out var old)) return Task.FromResult<MessageTemplate?>(null); var item = old with { Name = name, Body = body, UpdatedAt = DateTimeOffset.UtcNow }; items[id] = item; return Task.FromResult<MessageTemplate?>(item); }
        public Task<bool> DeleteAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default) => Task.FromResult(items.Remove(id));
    }

    private sealed class Portal : ITenantPortalRepository
    {
        public Task<TenantOverview?> GetOverviewAsync(Guid tenantId, CancellationToken cancellationToken) =>
            Task.FromResult<TenantOverview?>(new("Acme", "UTC", 0, 0, 0, 0, 0, []));
    }

    private sealed class AiSettings : ITenantAiSettingsRepository
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public Task<TenantAiSettings> GetAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TenantAiSettings("Improve prompt", "Validate prompt"));
        public Task SaveAsync(Guid tenantId, TenantAiSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class OllamaHandler : HttpMessageHandler
    {
        public string Response { get; set; } = """{"response":"ok"}""";
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public string? RequestBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(StatusCode) { Content = new StringContent(Response, Encoding.UTF8, "application/json") };
        }
    }
}
