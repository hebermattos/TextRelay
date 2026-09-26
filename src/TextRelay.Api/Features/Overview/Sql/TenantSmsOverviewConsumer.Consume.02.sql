-- Serialize transitions for the same message and apply only a newer state.
SELECT pg_advisory_xact_lock(hashtextextended(@MessageId::text, 0));

CREATE TEMP TABLE IF NOT EXISTS reporting_transition
(
    Accepted boolean,
    IsNew boolean,
    OldUserId uuid,
    OldUsername varchar(200),
    OldProvider varchar(50),
    OldDirection integer,
    OldQueueStatus integer,
    OldStatus integer
) ON COMMIT DROP;
TRUNCATE reporting_transition;

INSERT INTO reporting_transition
SELECT
    p.MessageId IS NULL OR @OccurredAtUtc > p.UpdatedAtUtc,
    p.MessageId IS NULL,
    p.UserId,p.Username,p.Provider,p.Direction,p.QueueStatus,p.Status
FROM (SELECT 1) seed
LEFT JOIN ReportingSmsMessages p ON p.MessageId=@MessageId;

INSERT INTO ReportingSmsMessages
(MessageId,TenantId,TenantName,UserId,Username,Provider,Direction,QueueStatus,Status,CreatedAtUtc,UpdatedAtUtc)
SELECT @MessageId,@TenantId,@TenantName,@UserId,@Username,@Provider,@Direction,@QueueStatus,@Status,@CreatedAtUtc,@OccurredAtUtc
WHERE (SELECT Accepted FROM reporting_transition)
ON CONFLICT (MessageId) DO UPDATE SET
 TenantName=EXCLUDED.TenantName,UserId=EXCLUDED.UserId,Username=EXCLUDED.Username,
 Provider=EXCLUDED.Provider,Direction=EXCLUDED.Direction,QueueStatus=EXCLUDED.QueueStatus,
 Status=EXCLUDED.Status,UpdatedAtUtc=EXCLUDED.UpdatedAtUtc;

-- Tenant-wide current totals.
INSERT INTO TenantSmsOverview (TenantId,Outbound,Inbound,Delivered,Failed,Pending,UpdatedAtUtc)
SELECT @TenantId,
 (CASE WHEN @Direction=1 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldDirection=1 THEN 1 ELSE 0 END),
 (CASE WHEN @Direction=2 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldDirection=2 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=3 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldStatus=3 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=4 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldStatus=4 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=1 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldStatus=1 THEN 1 ELSE 0 END),
 @OccurredAtUtc
FROM reporting_transition WHERE Accepted
ON CONFLICT (TenantId) DO UPDATE SET
 Outbound=TenantSmsOverview.Outbound+EXCLUDED.Outbound,Inbound=TenantSmsOverview.Inbound+EXCLUDED.Inbound,
 Delivered=TenantSmsOverview.Delivered+EXCLUDED.Delivered,Failed=TenantSmsOverview.Failed+EXCLUDED.Failed,
 Pending=TenantSmsOverview.Pending+EXCLUDED.Pending,UpdatedAtUtc=GREATEST(TenantSmsOverview.UpdatedAtUtc,EXCLUDED.UpdatedAtUtc);

-- Tenant/day transition. CreatedAtUtc is immutable for a message.
INSERT INTO TenantSmsDailyOverview
(ReportDate,TenantId,TenantName,TotalMessages,Scheduled,Queued,Sent,Delivered,Failed,Received,Outbound,Inbound,Pending,UpdatedAtUtc)
SELECT CAST(@CreatedAtUtc AT TIME ZONE 'UTC' AS date),@TenantId,@TenantName,
 CASE WHEN IsNew THEN 1 ELSE 0 END,
 (CASE WHEN @QueueStatus=3 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldQueueStatus=3 THEN 1 ELSE 0 END),
 (CASE WHEN @QueueStatus=2 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldQueueStatus=2 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=2 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldStatus=2 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=3 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldStatus=3 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=4 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldStatus=4 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=5 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldStatus=5 THEN 1 ELSE 0 END),
 (CASE WHEN @Direction=1 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldDirection=1 THEN 1 ELSE 0 END),
 (CASE WHEN @Direction=2 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldDirection=2 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=1 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldStatus=1 THEN 1 ELSE 0 END),@OccurredAtUtc
FROM reporting_transition WHERE Accepted
ON CONFLICT (ReportDate,TenantId) DO UPDATE SET
 TenantName=EXCLUDED.TenantName,TotalMessages=TenantSmsDailyOverview.TotalMessages+EXCLUDED.TotalMessages,
 Scheduled=TenantSmsDailyOverview.Scheduled+EXCLUDED.Scheduled,Queued=TenantSmsDailyOverview.Queued+EXCLUDED.Queued,
 Sent=TenantSmsDailyOverview.Sent+EXCLUDED.Sent,Delivered=TenantSmsDailyOverview.Delivered+EXCLUDED.Delivered,
 Failed=TenantSmsDailyOverview.Failed+EXCLUDED.Failed,Received=TenantSmsDailyOverview.Received+EXCLUDED.Received,
 Outbound=TenantSmsDailyOverview.Outbound+EXCLUDED.Outbound,Inbound=TenantSmsDailyOverview.Inbound+EXCLUDED.Inbound,
 Pending=TenantSmsDailyOverview.Pending+EXCLUDED.Pending,UpdatedAtUtc=GREATEST(TenantSmsDailyOverview.UpdatedAtUtc,EXCLUDED.UpdatedAtUtc);

-- Provider/day: subtract the previous dimension when provider changes, then add the current dimension.
INSERT INTO ProviderSmsDailyOverview
(ReportDate,TenantId,TenantName,Provider,TotalMessages,Scheduled,Queued,Sent,Delivered,Failed,Received,Outbound,Inbound,Pending,UpdatedAtUtc)
SELECT CAST(@CreatedAtUtc AT TIME ZONE 'UTC' AS date),@TenantId,@TenantName,OldProvider,-1,
 -(OldQueueStatus=3)::int,-(OldQueueStatus=2)::int,-(OldStatus=2)::int,-(OldStatus=3)::int,-(OldStatus=4)::int,
 -(OldStatus=5)::int,-(OldDirection=1)::int,-(OldDirection=2)::int,-(OldStatus=1)::int,@OccurredAtUtc
FROM reporting_transition WHERE Accepted AND NOT IsNew AND OldProvider<>@Provider
ON CONFLICT (ReportDate,TenantId,Provider) DO UPDATE SET
 TotalMessages=ProviderSmsDailyOverview.TotalMessages+EXCLUDED.TotalMessages,Scheduled=ProviderSmsDailyOverview.Scheduled+EXCLUDED.Scheduled,
 Queued=ProviderSmsDailyOverview.Queued+EXCLUDED.Queued,Sent=ProviderSmsDailyOverview.Sent+EXCLUDED.Sent,
 Delivered=ProviderSmsDailyOverview.Delivered+EXCLUDED.Delivered,Failed=ProviderSmsDailyOverview.Failed+EXCLUDED.Failed,
 Received=ProviderSmsDailyOverview.Received+EXCLUDED.Received,Outbound=ProviderSmsDailyOverview.Outbound+EXCLUDED.Outbound,
 Inbound=ProviderSmsDailyOverview.Inbound+EXCLUDED.Inbound,Pending=ProviderSmsDailyOverview.Pending+EXCLUDED.Pending,
 UpdatedAtUtc=GREATEST(ProviderSmsDailyOverview.UpdatedAtUtc,EXCLUDED.UpdatedAtUtc);

INSERT INTO ProviderSmsDailyOverview
(ReportDate,TenantId,TenantName,Provider,TotalMessages,Scheduled,Queued,Sent,Delivered,Failed,Received,Outbound,Inbound,Pending,UpdatedAtUtc)
SELECT CAST(@CreatedAtUtc AT TIME ZONE 'UTC' AS date),@TenantId,@TenantName,@Provider,
 CASE WHEN IsNew OR OldProvider<>@Provider THEN 1 ELSE 0 END,
 (CASE WHEN @QueueStatus=3 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldProvider=@Provider AND OldQueueStatus=3 THEN 1 ELSE 0 END),
 (CASE WHEN @QueueStatus=2 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldProvider=@Provider AND OldQueueStatus=2 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=2 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldProvider=@Provider AND OldStatus=2 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=3 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldProvider=@Provider AND OldStatus=3 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=4 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldProvider=@Provider AND OldStatus=4 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=5 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldProvider=@Provider AND OldStatus=5 THEN 1 ELSE 0 END),
 (CASE WHEN @Direction=1 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldProvider=@Provider AND OldDirection=1 THEN 1 ELSE 0 END),
 (CASE WHEN @Direction=2 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldProvider=@Provider AND OldDirection=2 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=1 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldProvider=@Provider AND OldStatus=1 THEN 1 ELSE 0 END),@OccurredAtUtc
FROM reporting_transition WHERE Accepted
ON CONFLICT (ReportDate,TenantId,Provider) DO UPDATE SET
 TenantName=EXCLUDED.TenantName,TotalMessages=ProviderSmsDailyOverview.TotalMessages+EXCLUDED.TotalMessages,
 Scheduled=ProviderSmsDailyOverview.Scheduled+EXCLUDED.Scheduled,Queued=ProviderSmsDailyOverview.Queued+EXCLUDED.Queued,
 Sent=ProviderSmsDailyOverview.Sent+EXCLUDED.Sent,Delivered=ProviderSmsDailyOverview.Delivered+EXCLUDED.Delivered,
 Failed=ProviderSmsDailyOverview.Failed+EXCLUDED.Failed,Received=ProviderSmsDailyOverview.Received+EXCLUDED.Received,
 Outbound=ProviderSmsDailyOverview.Outbound+EXCLUDED.Outbound,Inbound=ProviderSmsDailyOverview.Inbound+EXCLUDED.Inbound,
 Pending=ProviderSmsDailyOverview.Pending+EXCLUDED.Pending,UpdatedAtUtc=GREATEST(ProviderSmsDailyOverview.UpdatedAtUtc,EXCLUDED.UpdatedAtUtc);

-- User/day follows the same transition rule, including reassignment.
INSERT INTO UserSmsOverview
(TenantId,UserId,Username,ReportDate,TotalMessages,Scheduled,Queued,Sent,Delivered,Failed,Received,Outbound,Inbound,Pending,UpdatedAtUtc)
SELECT @TenantId,OldUserId,COALESCE(OldUsername,''),CAST(@CreatedAtUtc AT TIME ZONE 'UTC' AS date),-1,
 -(OldQueueStatus=3)::int,-(OldQueueStatus=2)::int,-(OldStatus=2)::int,-(OldStatus=3)::int,-(OldStatus=4)::int,
 -(OldStatus=5)::int,-(OldDirection=1)::int,-(OldDirection=2)::int,-(OldStatus=1)::int,@OccurredAtUtc
FROM reporting_transition WHERE Accepted AND NOT IsNew AND OldUserId IS NOT NULL AND OldUserId IS DISTINCT FROM @UserId
ON CONFLICT (TenantId,UserId,ReportDate) DO UPDATE SET
 TotalMessages=UserSmsOverview.TotalMessages+EXCLUDED.TotalMessages,Scheduled=UserSmsOverview.Scheduled+EXCLUDED.Scheduled,
 Queued=UserSmsOverview.Queued+EXCLUDED.Queued,Sent=UserSmsOverview.Sent+EXCLUDED.Sent,Delivered=UserSmsOverview.Delivered+EXCLUDED.Delivered,
 Failed=UserSmsOverview.Failed+EXCLUDED.Failed,Received=UserSmsOverview.Received+EXCLUDED.Received,
 Outbound=UserSmsOverview.Outbound+EXCLUDED.Outbound,Inbound=UserSmsOverview.Inbound+EXCLUDED.Inbound,
 Pending=UserSmsOverview.Pending+EXCLUDED.Pending,UpdatedAtUtc=GREATEST(UserSmsOverview.UpdatedAtUtc,EXCLUDED.UpdatedAtUtc);

INSERT INTO UserSmsOverview
(TenantId,UserId,Username,ReportDate,TotalMessages,Scheduled,Queued,Sent,Delivered,Failed,Received,Outbound,Inbound,Pending,UpdatedAtUtc)
SELECT @TenantId,@UserId,COALESCE(@Username,''),CAST(@CreatedAtUtc AT TIME ZONE 'UTC' AS date),
 CASE WHEN IsNew OR OldUserId IS DISTINCT FROM @UserId THEN 1 ELSE 0 END,
 (CASE WHEN @QueueStatus=3 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldUserId=@UserId AND OldQueueStatus=3 THEN 1 ELSE 0 END),
 (CASE WHEN @QueueStatus=2 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldUserId=@UserId AND OldQueueStatus=2 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=2 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldUserId=@UserId AND OldStatus=2 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=3 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldUserId=@UserId AND OldStatus=3 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=4 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldUserId=@UserId AND OldStatus=4 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=5 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldUserId=@UserId AND OldStatus=5 THEN 1 ELSE 0 END),
 (CASE WHEN @Direction=1 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldUserId=@UserId AND OldDirection=1 THEN 1 ELSE 0 END),
 (CASE WHEN @Direction=2 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldUserId=@UserId AND OldDirection=2 THEN 1 ELSE 0 END),
 (CASE WHEN @Status=1 THEN 1 ELSE 0 END)-(CASE WHEN NOT IsNew AND OldUserId=@UserId AND OldStatus=1 THEN 1 ELSE 0 END),@OccurredAtUtc
FROM reporting_transition WHERE Accepted AND @UserId IS NOT NULL
ON CONFLICT (TenantId,UserId,ReportDate) DO UPDATE SET
 Username=EXCLUDED.Username,TotalMessages=UserSmsOverview.TotalMessages+EXCLUDED.TotalMessages,
 Scheduled=UserSmsOverview.Scheduled+EXCLUDED.Scheduled,Queued=UserSmsOverview.Queued+EXCLUDED.Queued,
 Sent=UserSmsOverview.Sent+EXCLUDED.Sent,Delivered=UserSmsOverview.Delivered+EXCLUDED.Delivered,
 Failed=UserSmsOverview.Failed+EXCLUDED.Failed,Received=UserSmsOverview.Received+EXCLUDED.Received,
 Outbound=UserSmsOverview.Outbound+EXCLUDED.Outbound,Inbound=UserSmsOverview.Inbound+EXCLUDED.Inbound,
 Pending=UserSmsOverview.Pending+EXCLUDED.Pending,UpdatedAtUtc=GREATEST(UserSmsOverview.UpdatedAtUtc,EXCLUDED.UpdatedAtUtc);
