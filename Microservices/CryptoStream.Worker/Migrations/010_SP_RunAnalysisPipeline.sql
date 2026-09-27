-- Migration: 010_SP_RunAnalysisPipeline
-- Master stored procedure that orchestrates all crypto analysis SPs in correct order.
-- Single entry point for Hangfire recurring job — one job instead of six.

CREATE OR ALTER PROCEDURE crypto.sp_RunAnalysisPipeline
    @Symbol VARCHAR(20) = 'BTCUSDT',
    @Timeframe VARCHAR(4) = '1m',
    @LookbackMinutes INT = 5,
    @MaPeriod INT = 14,
    @VolPeriod INT = 14
AS
BEGIN
    SET NOCOUNT ON;

    -- 1. Aggregate raw trades into OHLC candles (must run first — all others depend on OHLC)
    EXEC crypto.sp_AggregateOHLC
        @Symbol = @Symbol,
        @Timeframe = @Timeframe,
        @LookbackMinutes = @LookbackMinutes;

    -- 2. Calculate moving averages (SMA + EMA) from OHLC
    EXEC crypto.sp_CalcMovingAverages
        @Symbol = @Symbol,
        @Timeframe = @Timeframe,
        @Period = @MaPeriod;

    -- 3. Calculate volatility metrics (StdDev, ATR, Range%) from OHLC
    EXEC crypto.sp_CalcVolatility
        @Symbol = @Symbol,
        @Timeframe = @Timeframe,
        @Period = @VolPeriod;

    -- 4. Update portfolio P&L with latest prices (no params — scans all OPEN positions)
    EXEC crypto.sp_UpdatePortfolio;

    -- 5. Check and trigger alerts (no params — scans all ACTIVE alerts)
    EXEC crypto.sp_CheckAlerts;
END
GO
