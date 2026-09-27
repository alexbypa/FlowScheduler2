-- Migration: 005_SP_CalcMovingAverages
-- Creates stored procedure crypto.sp_CalcMovingAverages.
-- Calculates SMA (self-join rolling average) and EMA (recursive CTE)
-- on crypto.OHLC.close_price, upserts into crypto.MovingAverages.
--
-- ponytail: Recursive CTE EMA will blow the default 100-recursion limit
-- and risks stack overflow on datasets > ~32K rows per symbol/timeframe.
-- OPTION (MAXRECURSION 0) removes the cap — deliberate stress-test
-- for lock escalation diagnostics. If prod perf matters, switch to
-- cursor-based or iterative UPDATE approach.

CREATE OR ALTER PROCEDURE crypto.sp_CalcMovingAverages
    @Symbol    VARCHAR(20),
    @Timeframe VARCHAR(4),
    @Period    INT = 14
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @K FLOAT = 2.0 / (@Period + 1);

    ---------------------------------------------------------------------------
    -- 1. Number all OHLC rows for this symbol/timeframe
    ---------------------------------------------------------------------------
    ;WITH Numbered AS (
        SELECT
            bucket_start,
            close_price,
            ROW_NUMBER() OVER (ORDER BY bucket_start) AS rn
        FROM crypto.OHLC
        WHERE symbol    = @Symbol
          AND timeframe = @Timeframe
    ),

    ---------------------------------------------------------------------------
    -- 2. SMA — self-join rolling average (window frames can't use variables)
    ---------------------------------------------------------------------------
    SMA AS (
        SELECT
            n.bucket_start,
            AVG(p.close_price) AS sma_value
        FROM Numbered n
        INNER JOIN Numbered p
            ON p.rn BETWEEN n.rn - @Period + 1 AND n.rn
        WHERE n.rn >= @Period
        GROUP BY n.bucket_start, n.rn
    ),

    ---------------------------------------------------------------------------
    -- 3. EMA — recursive CTE
    --    Anchor: first SMA value (at row @Period)
    --    Recurse: price * K + prev_ema * (1 - K)
    ---------------------------------------------------------------------------
    EMA_Anchor AS (
        SELECT
            n.bucket_start,
            n.close_price,
            n.rn,
            CAST(s.sma_value AS FLOAT) AS ema_value
        FROM Numbered n
        INNER JOIN SMA s ON s.bucket_start = n.bucket_start
        WHERE n.rn = @Period
    ),
    EMA AS (
        -- Anchor
        SELECT bucket_start, close_price, rn, ema_value
        FROM EMA_Anchor

        UNION ALL

        -- Recursive step
        SELECT
            n.bucket_start,
            n.close_price,
            n.rn,
            CAST(n.close_price * @K + e.ema_value * (1.0 - @K) AS FLOAT)
        FROM EMA e
        INNER JOIN Numbered n ON n.rn = e.rn + 1
    )

    ---------------------------------------------------------------------------
    -- 4. Stash EMA results (needed because MERGE can't follow recursive CTE
    --    with MAXRECURSION directly in two separate statements)
    ---------------------------------------------------------------------------
    SELECT bucket_start, ema_value
    INTO #EMA_Results
    FROM EMA
    OPTION (MAXRECURSION 0);

    ---------------------------------------------------------------------------
    -- 5. MERGE upsert — SMA
    ---------------------------------------------------------------------------
    MERGE crypto.MovingAverages AS target
    USING SMA AS src
        ON target.symbol       = @Symbol
       AND target.timeframe    = @Timeframe
       AND target.bucket_start = src.bucket_start
       AND target.ma_type      = 'SMA'
       AND target.period       = @Period

    WHEN MATCHED THEN UPDATE SET
        value      = src.sma_value,
        updated_at = SYSUTCDATETIME()

    WHEN NOT MATCHED THEN INSERT
        (symbol, timeframe, bucket_start, ma_type, period, value, updated_at)
    VALUES
        (@Symbol, @Timeframe, src.bucket_start, 'SMA', @Period, src.sma_value, SYSUTCDATETIME());

    ---------------------------------------------------------------------------
    -- 6. MERGE upsert — EMA
    ---------------------------------------------------------------------------
    MERGE crypto.MovingAverages AS target
    USING #EMA_Results AS src
        ON target.symbol       = @Symbol
       AND target.timeframe    = @Timeframe
       AND target.bucket_start = src.bucket_start
       AND target.ma_type      = 'EMA'
       AND target.period       = @Period

    WHEN MATCHED THEN UPDATE SET
        value      = src.ema_value,
        updated_at = SYSUTCDATETIME()

    WHEN NOT MATCHED THEN INSERT
        (symbol, timeframe, bucket_start, ma_type, period, value, updated_at)
    VALUES
        (@Symbol, @Timeframe, src.bucket_start, 'EMA', @Period, src.ema_value, SYSUTCDATETIME());

    DROP TABLE #EMA_Results;
END
GO
