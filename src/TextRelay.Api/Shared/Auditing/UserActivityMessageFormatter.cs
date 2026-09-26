namespace Sms.Api.Middleware;

internal enum UserActivityKind { PageView, DataChange, Action }

internal sealed record UserActivityDescription(string Success, string Failure, UserActivityKind Kind);

internal static class UserActivityMessageFormatter
{
    private static readonly Dictionary<(string Controller, string Action), UserActivityDescription> Messages = new()
    {
        [("PortalAuth", "Logout")] = Action("Signed out of the portal.", "Could not sign out of the portal."),
        [("Messages", "Send")] = Change("Sent an SMS message.", "Could not send an SMS message."),
        [("TenantUsers", "Create")] = Change("Created a tenant user.", "Could not create the tenant user."),
        [("TenantUsers", "SetState")] = Change("Updated a tenant user's status.", "Could not update the tenant user's status."),
        [("TenantUsers", "ResetPassword")] = Change("Reset a tenant user's password.", "Could not reset the tenant user's password."),
        [("MessageTemplates", "Create")] = Change("Created a message template.", "Could not create the message template."),
        [("MessageTemplates", "Update")] = Change("Updated a message template.", "Could not update the message template."),
        [("MessageTemplates", "Delete")] = Change("Deleted a message template.", "Could not delete the message template."),
        [("MessageTemplates", "Render")] = Action("Rendered a message template.", "Could not render the message template."),
        [("MessageAssistant", "Improve")] = Action("Used AI to improve a message.", "Could not improve the message with AI."),
        [("MessageAssistant", "Validate")] = Action("Used AI to validate a message.", "Could not validate the message with AI."),
        [("Alerts", "CreateRule")] = Change("Created an alert rule.", "Could not create the alert rule."),
        [("Alerts", "UpdateRule")] = Change("Updated an alert rule.", "Could not update the alert rule."),
        [("Alerts", "DeleteRule")] = Change("Deleted an alert rule.", "Could not delete the alert rule."),
        [("Alerts", "MarkRead")] = Change("Marked an alert as read.", "Could not mark the alert as read."),
        [("Alerts", "MarkAllRead")] = Change("Marked all alerts as read.", "Could not mark all alerts as read."),
        [("OptOuts", "Add")] = Change("Blocked a number from receiving SMS messages.", "Could not block the number."),
        [("OptOuts", "Import")] = Change("Imported SMS opt-outs.", "Could not import SMS opt-outs."),
        [("OptOuts", "Export")] = Action("Exported the SMS opt-out list.", "Could not export the SMS opt-out list."),
        [("OptOuts", "Remove")] = Change("Removed a number from the SMS opt-out list.", "Could not remove the number.")
    };

    internal static UserActivityDescription? Format(string? controller, string? action)
    {
        if (controller is null || action is null)
            return null;

        return Messages.TryGetValue((controller, action), out var description)
            ? description
            : null;
    }

    private static UserActivityDescription Change(string success, string failure) => new(success, failure, UserActivityKind.DataChange);
    private static UserActivityDescription Action(string success, string failure) => new(success, failure, UserActivityKind.Action);
}
