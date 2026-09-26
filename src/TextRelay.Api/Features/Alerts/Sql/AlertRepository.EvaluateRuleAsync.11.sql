SELECT COUNT(*)::INTEGER
FROM AlertMessageWindow
WHERE TenantId=@TenantId
  AND Status=@Status
  AND OccurredAtUtc >= @OccurredAtUtc - make_interval(mins => @WindowMinutes)
  AND OccurredAtUtc <= @OccurredAtUtc
  AND (@Provider IS NULL OR Provider=@Provider);
