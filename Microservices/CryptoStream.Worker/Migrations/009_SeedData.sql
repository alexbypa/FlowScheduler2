-- Migration: 009_SeedData
-- Seeds test data for crypto.Portfolio and crypto.Alerts tables.
-- Idempotent: inserts only when tables are empty.

-----------------------------------------------------------------------
-- 1. crypto.Portfolio — 8 simulated positions
-----------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM crypto.Portfolio)
BEGIN
    INSERT INTO crypto.Portfolio (symbol, side, entry_price, quantity, current_price, unrealized_pnl, pnl_pct, status)
    VALUES
        -- OPEN positions with pre-calculated P&L
        ('BTCUSDT',  'LONG',  63500.00000000, 0.15000000, 65200.00000000,  255.00000000,   2.6772, 'OPEN'),
        ('ETHUSDT',  'LONG',   3280.00000000, 2.50000000,  3420.00000000,  350.00000000,   4.2683, 'OPEN'),
        ('SOLUSDT',  'SHORT',   152.00000000, 20.00000000,  145.00000000,  140.00000000,   4.6053, 'OPEN'),

        -- OPEN positions with zero P&L (will be updated by SP)
        ('BNBUSDT',  'LONG',   575.00000000, 5.00000000,    0.00000000,    0.00000000,   0.0000, 'OPEN'),
        ('XRPUSDT',  'SHORT',    0.58000000, 5000.00000000, 0.00000000,    0.00000000,   0.0000, 'OPEN'),

        -- CLOSED positions with final P&L
        ('BTCUSDT',  'SHORT', 67000.00000000, 0.10000000, 65100.00000000,  190.00000000,   2.8358, 'CLOSED'),
        ('ETHUSDT',  'SHORT',  3500.00000000, 1.00000000,  3420.00000000,   80.00000000,   2.2857, 'CLOSED'),
        ('SOLUSDT',  'LONG',   138.00000000, 15.00000000,  145.00000000,  105.00000000,   5.0725, 'CLOSED');
END
GO

-----------------------------------------------------------------------
-- 2. crypto.Alerts — 12 threshold alerts
-----------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM crypto.Alerts)
BEGIN
    INSERT INTO crypto.Alerts (symbol, alert_type, threshold, current_value, status, message, triggered_at, expires_at)
    VALUES
        -- ACTIVE alerts (to be checked by SP)
        ('BTCUSDT',  'PRICE_ABOVE', 66000.00000000,     0.00000000, 'ACTIVE',    N'BTC breakout above 66K',          NULL, '2026-10-15'),
        ('BTCUSDT',  'PRICE_BELOW', 62000.00000000,     0.00000000, 'ACTIVE',    N'BTC support break below 62K',     NULL, '2026-10-15'),
        ('ETHUSDT',  'PRICE_ABOVE',  3500.00000000,     0.00000000, 'ACTIVE',    N'ETH resistance test at 3500',     NULL, '2026-10-10'),
        ('SOLUSDT',  'PRICE_BELOW',   140.00000000,     0.00000000, 'ACTIVE',    N'SOL dip below 140',               NULL,  NULL),
        ('BNBUSDT',  'PRICE_ABOVE',   600.00000000,     0.00000000, 'ACTIVE',    N'BNB breaks 600 barrier',          NULL, '2026-11-01'),
        ('XRPUSDT',  'VOL_SPIKE',       2.50000000,     0.00000000, 'ACTIVE',    N'XRP volume spike > 2.5x avg',     NULL,  NULL),
        ('BTCUSDT',  'VOL_SPIKE',       3.00000000,     0.00000000, 'ACTIVE',    N'BTC volume spike > 3x avg',       NULL, '2026-10-20'),

        -- TRIGGERED alerts
        ('ETHUSDT',  'PRICE_BELOW',  3300.00000000,  3285.00000000, 'TRIGGERED', N'ETH dropped below 3300',          '2026-09-25 14:32:00', '2026-10-01'),
        ('SOLUSDT',  'PRICE_ABOVE',   150.00000000,   152.40000000, 'TRIGGERED', N'SOL broke above 150',             '2026-09-26 09:15:00', '2026-10-05'),
        ('BNBUSDT',  'VOL_SPIKE',       2.00000000,     2.30000000, 'TRIGGERED', N'BNB volume spike detected',       '2026-09-24 22:10:00',  NULL),

        -- EXPIRED alerts
        ('XRPUSDT',  'PRICE_ABOVE',     0.60000000,     0.55000000, 'EXPIRED',   N'XRP above 0.60 — expired',        NULL, '2026-09-20'),
        ('BTCUSDT',  'PRICE_BELOW', 60000.00000000,     0.00000000, 'EXPIRED',   N'BTC crash alert — expired',       NULL, '2026-09-22');
END
GO
