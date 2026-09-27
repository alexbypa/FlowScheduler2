-- Migration: 007_SP_UpdatePortfolio
-- Creates stored procedure crypto.sp_UpdatePortfolio.
-- Updates unrealized P&L for all OPEN portfolio positions
-- using the latest 1m OHLC close price per symbol.

CREATE OR ALTER PROCEDURE crypto.sp_UpdatePortfolio
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE p
    SET
        current_price  = latest.close_price,
        unrealized_pnl = CASE p.side
                            WHEN 'LONG'  THEN (latest.close_price - p.entry_price) * p.quantity
                            WHEN 'SHORT' THEN (p.entry_price - latest.close_price) * p.quantity
                         END,
        pnl_pct        = CASE p.side
                            WHEN 'LONG'  THEN (latest.close_price - p.entry_price) * p.quantity
                            WHEN 'SHORT' THEN (p.entry_price - latest.close_price) * p.quantity
                         END
                         / NULLIF(p.entry_price * p.quantity, 0) * 100,
        updated_at     = SYSUTCDATETIME()
    FROM crypto.Portfolio p
    CROSS APPLY (
        SELECT TOP 1 o.close_price
        FROM crypto.OHLC o
        WHERE o.timeframe = '1m'
          AND o.symbol = p.symbol
        ORDER BY o.bucket_start DESC
    ) AS latest
    WHERE p.status = 'OPEN';
END
GO
