-- Migration: 004_SP_AggregateOHLC
-- Creates stored procedure crypto.sp_AggregateOHLC.
-- Aggregates raw trades from crypto.CryptoTrades into OHLC candlesticks
-- using MERGE upsert into crypto.OHLC.
--
-- ponytail: Deliberately uses correlated TOP 1 subqueries for open/close
-- price instead of FIRST_VALUE/LAST_VALUE window functions. This is O(n²)
-- on large buckets — intentional for SqlDiagnosticsTool stress testing.

CREATE OR ALTER PROCEDURE crypto.sp_AggregateOHLC
    @Symbol          VARCHAR(20),
    @Timeframe       VARCHAR(4),
    @LookbackMinutes INT = 60
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @BucketSeconds INT = CASE @Timeframe
        WHEN '1m'  THEN 60
        WHEN '5m'  THEN 300
        WHEN '15m' THEN 900
        WHEN '1h'  THEN 3600
    END;

    DECLARE @CutoffMs BIGINT = DATEDIFF_BIG(SECOND, '1970-01-01', SYSUTCDATETIME()) * 1000
                              - (@LookbackMinutes * 60 * 1000);

    ;WITH Buckets AS (
        SELECT
            -- Floor trade_time to bucket boundary (epoch ms → seconds, floor, back to datetime)
            DATEADD(SECOND,
                (t.trade_time / 1000 / @BucketSeconds) * @BucketSeconds,
                CAST('1970-01-01' AS DATETIME2)
            ) AS bucket_start,

            -- ponytail: correlated subquery for open_price — O(n²), upgrade to FIRST_VALUE if perf matters
            (SELECT TOP 1 t2.price
             FROM crypto.CryptoTrades t2
             WHERE t2.symbol = @Symbol
               AND t2.trade_time >= @CutoffMs
               AND (t2.trade_time / 1000 / @BucketSeconds) = (t.trade_time / 1000 / @BucketSeconds)
             ORDER BY t2.trade_time ASC
            ) AS open_price,

            -- ponytail: correlated subquery for close_price — O(n²), upgrade to LAST_VALUE if perf matters
            (SELECT TOP 1 t3.price
             FROM crypto.CryptoTrades t3
             WHERE t3.symbol = @Symbol
               AND t3.trade_time >= @CutoffMs
               AND (t3.trade_time / 1000 / @BucketSeconds) = (t.trade_time / 1000 / @BucketSeconds)
             ORDER BY t3.trade_time DESC
            ) AS close_price,

            MAX(t.price)    AS high_price,
            MIN(t.price)    AS low_price,
            SUM(t.quantity) AS volume,
            COUNT(*)        AS trade_count
        FROM crypto.CryptoTrades t
        WHERE t.symbol = @Symbol
          AND t.trade_time >= @CutoffMs
        GROUP BY (t.trade_time / 1000 / @BucketSeconds)
    )
    MERGE crypto.OHLC AS target
    USING Buckets AS src
        ON target.symbol       = @Symbol
       AND target.timeframe    = @Timeframe
       AND target.bucket_start = src.bucket_start

    WHEN MATCHED THEN UPDATE SET
        close_price = src.close_price,
        high_price  = src.high_price,
        low_price   = src.low_price,
        volume      = src.volume,
        trade_count = src.trade_count,
        updated_at  = SYSUTCDATETIME()

    WHEN NOT MATCHED THEN INSERT
        (symbol, timeframe, bucket_start, open_price, high_price, low_price, close_price, volume, trade_count, updated_at)
    VALUES
        (@Symbol, @Timeframe, src.bucket_start, src.open_price, src.high_price, src.low_price, src.close_price, src.volume, src.trade_count, SYSUTCDATETIME());
END
GO
