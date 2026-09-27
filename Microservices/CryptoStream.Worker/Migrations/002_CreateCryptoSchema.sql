-- Migration: 002_CreateCryptoSchema
-- Creates the 'crypto' schema and transfers dbo.CryptoTrades into it.
-- Idempotent: safe to re-run.

-- Create schema if not exists
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'crypto')
BEGIN
    EXEC('CREATE SCHEMA crypto');
END
GO

-- Transfer CryptoTrades from dbo to crypto schema
IF EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CryptoTrades' AND schema_id = SCHEMA_ID('dbo'))
   AND NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'CryptoTrades' AND schema_id = SCHEMA_ID('crypto'))
BEGIN
    ALTER SCHEMA crypto TRANSFER dbo.CryptoTrades;
END
GO
