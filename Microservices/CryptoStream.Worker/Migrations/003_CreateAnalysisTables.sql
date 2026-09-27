-- Migration: 003_CreateAnalysisTables
-- Creates 5 analysis tables in the 'crypto' schema.
-- Idempotent: all CREATE wrapped in IF NOT EXISTS.

-----------------------------------------------------------------------
-- 1. crypto.OHLC — Candlestick aggregates (1m/5m/15m/1h)
-----------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'OHLC' AND schema_id = SCHEMA_ID('crypto'))
BEGIN
    CREATE TABLE crypto.OHLC
    (
        id              INT IDENTITY(1,1) PRIMARY KEY,
        symbol          VARCHAR(20)     NOT NULL,
        timeframe       VARCHAR(4)      NOT NULL,
        bucket_start    DATETIME2       NOT NULL,
        open_price      DECIMAL(18,8)   NOT NULL,
        high_price      DECIMAL(18,8)   NOT NULL,
        low_price       DECIMAL(18,8)   NOT NULL,
        close_price     DECIMAL(18,8)   NOT NULL,
        volume          DECIMAL(18,8)   NOT NULL DEFAULT 0,
        trade_count     INT             NOT NULL DEFAULT 0,
        created_at      DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at      DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );

    CREATE UNIQUE INDEX UQ_OHLC_Symbol_TF_Bucket
        ON crypto.OHLC (symbol, timeframe, bucket_start);

    CREATE NONCLUSTERED INDEX IX_OHLC_BucketDesc
        ON crypto.OHLC (bucket_start DESC);
END
GO

-----------------------------------------------------------------------
-- 2. crypto.MovingAverages — SMA / EMA values
-----------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'MovingAverages' AND schema_id = SCHEMA_ID('crypto'))
BEGIN
    CREATE TABLE crypto.MovingAverages
    (
        id              INT IDENTITY(1,1) PRIMARY KEY,
        symbol          VARCHAR(20)     NOT NULL,
        timeframe       VARCHAR(4)      NOT NULL,
        bucket_start    DATETIME2       NOT NULL,
        ma_type         VARCHAR(4)      NOT NULL,  -- 'SMA' or 'EMA'
        period          INT             NOT NULL,
        value           DECIMAL(18,8)   NOT NULL,
        created_at      DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at      DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );

    CREATE UNIQUE INDEX UQ_MA_Symbol_TF_Bucket_Type_Period
        ON crypto.MovingAverages (symbol, timeframe, bucket_start, ma_type, period);
END
GO

-----------------------------------------------------------------------
-- 3. crypto.Volatility — StdDev, ATR, Range%
-----------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Volatility' AND schema_id = SCHEMA_ID('crypto'))
BEGIN
    CREATE TABLE crypto.Volatility
    (
        id              INT IDENTITY(1,1) PRIMARY KEY,
        symbol          VARCHAR(20)     NOT NULL,
        timeframe       VARCHAR(4)      NOT NULL,
        bucket_start    DATETIME2       NOT NULL,
        period          INT             NOT NULL,
        std_dev         DECIMAL(18,8)   NOT NULL DEFAULT 0,
        atr             DECIMAL(18,8)   NOT NULL DEFAULT 0,
        range_pct       DECIMAL(10,4)   NOT NULL DEFAULT 0,
        created_at      DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at      DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );

    CREATE UNIQUE INDEX UQ_Vol_Symbol_TF_Bucket_Period
        ON crypto.Volatility (symbol, timeframe, bucket_start, period);
END
GO

-----------------------------------------------------------------------
-- 4. crypto.Portfolio — Simulated positions + P&L
-----------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Portfolio' AND schema_id = SCHEMA_ID('crypto'))
BEGIN
    CREATE TABLE crypto.Portfolio
    (
        id              INT IDENTITY(1,1) PRIMARY KEY,
        symbol          VARCHAR(20)     NOT NULL,
        side            VARCHAR(5)      NOT NULL,  -- 'LONG' or 'SHORT'
        entry_price     DECIMAL(18,8)   NOT NULL,
        quantity        DECIMAL(18,8)   NOT NULL,
        current_price   DECIMAL(18,8)   NOT NULL DEFAULT 0,
        unrealized_pnl  DECIMAL(18,8)   NOT NULL DEFAULT 0,
        pnl_pct         DECIMAL(10,4)   NOT NULL DEFAULT 0,
        status          VARCHAR(10)     NOT NULL DEFAULT 'OPEN',  -- 'OPEN' or 'CLOSED'
        created_at      DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at      DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );

    CREATE NONCLUSTERED INDEX IX_Portfolio_Status_Symbol
        ON crypto.Portfolio (status, symbol);
END
GO

-----------------------------------------------------------------------
-- 5. crypto.Alerts — Price/volume threshold alerts
-- NOTE: Deliberately NO index on (symbol, status) to cause table scan
-- for SqlDiagnosticsTool stress-test. Only index on created_at DESC.
-----------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Alerts' AND schema_id = SCHEMA_ID('crypto'))
BEGIN
    CREATE TABLE crypto.Alerts
    (
        id              INT IDENTITY(1,1) PRIMARY KEY,
        symbol          VARCHAR(20)     NOT NULL,
        alert_type      VARCHAR(20)     NOT NULL,  -- 'PRICE_ABOVE', 'PRICE_BELOW', 'VOL_SPIKE'
        threshold       DECIMAL(18,8)   NOT NULL,
        current_value   DECIMAL(18,8)   NOT NULL DEFAULT 0,
        status          VARCHAR(15)     NOT NULL DEFAULT 'ACTIVE',  -- 'ACTIVE', 'TRIGGERED', 'EXPIRED'
        message         NVARCHAR(500)   NULL,
        triggered_at    DATETIME2       NULL,
        expires_at      DATETIME2       NULL,
        created_at      DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at      DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );

    -- ponytail: Only created_at index — deliberate missing (symbol, status) index for stress-test
    CREATE NONCLUSTERED INDEX IX_Alerts_CreatedDesc
        ON crypto.Alerts (created_at DESC);
END
GO
