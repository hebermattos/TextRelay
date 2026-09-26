INSERT INTO SmsOptOuts
    (Id, TenantId, PhoneHash, PhoneNumber, Source, Reason, CreatedAt)
VALUES
    (@Id, @TenantId, @PhoneHash, @PhoneNumber, @Source, @Reason, @OccurredAt)
ON CONFLICT (TenantId, PhoneHash)
DO UPDATE SET
    Source=EXCLUDED.Source,
    Reason=EXCLUDED.Reason,
    UpdatedAt=@OccurredAt;
