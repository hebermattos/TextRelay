SELECT EXISTS
(
    SELECT 1
    FROM SmsOptOuts
    WHERE TenantId=@TenantId AND PhoneHash=@PhoneHash
);
