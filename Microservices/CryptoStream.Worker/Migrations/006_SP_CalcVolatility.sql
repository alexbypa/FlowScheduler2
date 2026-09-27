-- Migration: 006_SP_CalcVolatility
-- Creates stored procedure crypto.sp_CalcVolatility.
-- Calculates rolling StdDev, ATR, and Range% from crypto.OHLC
-- and upserts results into crypto.Volatility via MERGE.
--
-- ponytail: StdDev uses self-join with manual variance formula because
-- STDEV() OVER (ROWS N PRECEDING) doesn't support variable window size
-- in SQL Server. Upgrade to CLR aggregate if precision/perf matters.

CREATE OR ALTER PROCEDURE crypto.sp_CalcVolatility
    @Symbol    VARCHAR(20),
    @Timeframe VARCHAR(4),
    @Period    INT = 14
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH Numbered AS (
        SELECT
            bucket_start,
            high_price,
            low_price,
            close_price,
            ROW_NUMBER() OVER (ORDER BY bucket_start) AS rn
        FROM crypto.OHLC
        WHERE symbol    = @Symbol
          AND timeframe = @Timeframe
    ),
    RollingStats AS (
        SELECT
            cur.bucket_start,
            cur.high_price,
            cur.low_price,
            cur.close_price,

            -- StdDev via manual formula: sqrt( avg(x²) - avg(x)² )
            -- ponytail: correlated subquery per row — O(n * @Period), fine for stress-test batches
            (SELECT
                CASE WHEN COUNT(*) < 2 THEN 0
                ELSE SQRT(
                    SUM(w.close_price * w.close_price) / COUNT(*)
                  - POWER(SUM(w.close_price) / COUNT(*), 2)
                )
                END
             FROM Numbered w
             WHERE w.rn BETWEEN cur.rn - @Period + 1 AND cur.rn
               AND w.rn >= 1
            ) AS std_dev,

            -- Simplified ATR: avg(high - low) over window
            (SELECT AVG(w.high_price - w.low_price)
             FROM Numbered w
             WHERE w.rn BETWEEN cur.rn - @Period + 1 AND cur.rn
               AND w.rn >= 1
            ) AS atr

        FROM Numbered cur
        WHERE cur.rn >= @Period
    )
    MERGE crypto.Volatility AS target
    USING (
        SELECT
            bucket_start,
            std_dev,
            atr,
            (high_price - low_price) / NULLIF(close_price, 0) * 100 AS range_pct
        FROM RollingStats
    ) AS src
        ON target.symbol       = @Symbol
       AND target.timeframe    = @Timeframe
       AND target.bucket_start = src.bucket_start
       AND target.period       = @Period

    WHEN MATCHED THEN UPDATE SET
        std_dev    = src.std_dev,
        atr        = src.atr,
        range_pct  = ISNULL(src.range_pct, 0),
        updated_at = SYSUTCDATETIME()

    WHEN NOT MATCHED THEN INSERT
        (symbol, timeframe, bucket_start, period, std_dev, atr, range_pct, updated_at)
    VALUES
        (@Symbol, @Timeframe, src.bucket_start, @Period, src.std_dev, src.atr, ISNULL(src.range_pct, 0), SYSUTCDATETIME());
END
GO
