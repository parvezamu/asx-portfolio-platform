-- ASX portfolio platform schema (Azure SQL / SQL Server 2022)
IF OBJECT_ID('dbo.DailyReturn') IS NOT NULL DROP TABLE dbo.DailyReturn;
IF OBJECT_ID('dbo.Company') IS NOT NULL DROP TABLE dbo.Company;

CREATE TABLE dbo.Company (
    Ticker   VARCHAR(10)   NOT NULL PRIMARY KEY,
    Name     NVARCHAR(100) NOT NULL,
    Sector   NVARCHAR(50)  NOT NULL
);

CREATE TABLE dbo.DailyReturn (
    Ticker    VARCHAR(10) NOT NULL REFERENCES dbo.Company(Ticker),
    TradeDate DATE        NOT NULL,
    Ret       FLOAT       NOT NULL,
    CONSTRAINT PK_DailyReturn PRIMARY KEY (Ticker, TradeDate)
);
CREATE INDEX IX_DailyReturn_Date ON dbo.DailyReturn (TradeDate) INCLUDE (Ret);
