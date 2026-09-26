DELETE FROM SmsOptOuts
WHERE TenantId=@TenantId AND PhoneHash=@PhoneHash;
