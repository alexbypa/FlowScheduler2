-- Migration: 008_SP_CheckAlerts
-- Creates stored procedure crypto.sp_CheckAlerts.
-- Expires stale alerts, then checks active alerts against latest OHLC prices
-- and triggers those meeting threshold conditions.
--
-- ponytail: No index on crypto.Alerts(symbol, status) — full table scan is
-- deliberate for SqlDiagnosticsTool stress testing. Add covering index
-- IX_Alerts_Symbol_Status if scan time matters in production.

CREATE OR ALTER PROCEDURE crypto.sp_CheckAlerts
AS
BEGIN
    SET NOCOUNT ON;

    -- Step 1: Expire old alerts past their expiration window
    UPDATE crypto.Alerts
    SET    status     = 'EXPIRED',
           updated_at = SYSUTCDATETIME()
    WHERE  status     = 'ACTIVE'
      AND  expires_at IS NOT NULL
      AND  expires_at <= SYSUTCDATETIME();

    -- Step 2: Check active alerts against latest 1m OHLC candle and trigger matches
    UPDATE a
    SET    a.status        = 'TRIGGERED',
           a.triggered_at  = SYSUTCDATETIME(),
           a.current_value = CASE a.alert_type
                                 WHEN 'VOL_SPIKE'   THEN latest.volume
                                 ELSE                     latest.close_price
                             END,
           a.message        = CASE a.alert_type
                                 WHEN 'PRICE_ABOVE' THEN a.symbol + ' crossed above '  + FORMAT(a.threshold, 'N2')
                                 WHEN 'PRICE_BELOW' THEN a.symbol + ' crossed below '  + FORMAT(a.threshold, 'N2')
                                 WHEN 'VOL_SPIKE'   THEN a.symbol + ' volume spiked past ' + FORMAT(a.threshold, 'N2')
                             END,
           a.updated_at     = SYSUTCDATETIME()
    FROM   crypto.Alerts a
    CROSS APPLY (
        SELECT TOP 1 o.close_price, o.high_price, o.low_price, o.volume
        FROM   crypto.OHLC o
        WHERE  o.timeframe = '1m'
          AND  o.symbol    = a.symbol
        ORDER BY o.bucket_start DESC
    ) AS latest
    WHERE  a.status = 'ACTIVE'
      AND  (
               (a.alert_type = 'PRICE_ABOVE' AND latest.close_price >= a.threshold)
            OR (a.alert_type = 'PRICE_BELOW' AND latest.close_price <= a.threshold)
            OR (a.alert_type = 'VOL_SPIKE'   AND latest.volume      >= a.threshold)
           );
END
GO
