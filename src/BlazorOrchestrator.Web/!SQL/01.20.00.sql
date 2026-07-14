/****** Blazor Data Orchestrator - Version 01.20.00 ******/
/****** Add Admin and ViewOnly roles                ******/
/****** All statements are idempotent (IF NOT EXISTS) *****/

SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

-- ============================================================
-- Step 1: Insert the Admin role if it does not already exist
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = N'ADMIN')
BEGIN
    INSERT INTO [dbo].[AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
    VALUES (NEWID(), N'Admin', N'ADMIN', NEWID())
END
GO

-- ============================================================
-- Step 2: Insert the ViewOnly role if it does not already exist
-- ============================================================

IF NOT EXISTS (SELECT 1 FROM [dbo].[AspNetRoles] WHERE [NormalizedName] = N'VIEWONLY')
BEGIN
    INSERT INTO [dbo].[AspNetRoles] ([Id], [Name], [NormalizedName], [ConcurrencyStamp])
    VALUES (NEWID(), N'ViewOnly', N'VIEWONLY', NEWID())
END
GO
