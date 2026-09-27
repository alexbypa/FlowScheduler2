-- Migration: 001_CreateCryptoTrades
-- Creates the target table for Binance trade stream data.
-- Consumed by FlowScheduler.RabbitMqConsumerCommand → DynamicJsonDbWriter.
-- Column names match the normalized JSON keys from BinanceWebSocketReader.NormalizeTradeJson().

CREATE TABLE CryptoTrades
(
    id                INT IDENTITY(1,1) PRIMARY KEY,
    event_type        VARCHAR(20)     NOT NULL,
    event_time        BIGINT          NOT NULL,
    symbol            VARCHAR(20)     NOT NULL,
    trade_id          BIGINT          NOT NULL,
    price             DECIMAL(18,8)   NOT NULL,
    quantity          DECIMAL(18,8)   NOT NULL,
    trade_time        BIGINT          NOT NULL,
    is_buyer_maker    BIT             NOT NULL,
    inserted_at       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Index for common queries: filter by symbol, order by trade time
CREATE NONCLUSTERED INDEX IX_CryptoTrades_Symbol_TradeTime
    ON CryptoTrades (symbol, trade_time DESC);

-- Prevent duplicate trades from at-least-once delivery
CREATE UNIQUE INDEX UQ_CryptoTrades_TradeId
    ON CryptoTrades (trade_id);
