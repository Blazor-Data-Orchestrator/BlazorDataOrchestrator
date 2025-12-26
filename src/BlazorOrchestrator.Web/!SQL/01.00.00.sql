SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = N'blazororchestratordb')
    CREATE DATABASE [blazororchestratordb];
GO
USE [blazororchestratordb];
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Article]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[Article](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[ArticleName] [nvarchar](250) NOT NULL,
 CONSTRAINT [PK_Article] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ArticleDetail]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[ArticleDetail](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[ArticleId] [int] NOT NULL,
	[ArticleSequence] [int] NOT NULL,
	[ArticleContent] [nvarchar](max) NULL,
 CONSTRAINT [PK_ArticleDetail] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ArticleVectorData]') AND type in (N'U'))
BEGIN
CREATE TABLE [dbo].[ArticleVectorData](
	[Id] [int] IDENTITY(1,1) NOT NULL,
	[ArticleDetailId] [int] NOT NULL,
	[vector_value_id] [int] NOT NULL,
	[vector_value] [float] NOT NULL,
 CONSTRAINT [PK_ArticleVectorData] PRIMARY KEY CLUSTERED 
(
	[Id] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[SimilarContentArticles]') AND type in (N'FN', N'IF', N'TF', N'FS', N'FT'))
BEGIN
execute dbo.sp_executesql @statement = N'/*
    From GitHub project: Azure-Samples/azure-sql-db-openai
*/
CREATE   function [dbo].[SimilarContentArticles](@vector nvarchar(max))
returns table
as
return with cteVector as
(
    select 
        cast([key] as int) as [vector_value_id],
        cast([value] as float) as [vector_value]
    from 
        openjson(@vector)
),
cteSimilar as
(
select top (10)
    v2.ArticleDetailId, 
    sum(v1.[vector_value] * v2.[vector_value]) / 
        (
            sqrt(sum(v1.[vector_value] * v1.[vector_value])) 
            * 
            sqrt(sum(v2.[vector_value] * v2.[vector_value]))
        ) as cosine_distance
from 
    cteVector v1
inner join 
    dbo.ArticleVectorData v2 on v1.vector_value_id = v2.vector_value_id
group by
    v2.ArticleDetailId
order by
    cosine_distance desc
)
select 
    (select [ArticleName] from [Article] where id = a.ArticleId) as ArticleName,
    a.ArticleContent,
    a.ArticleSequence,
    r.cosine_distance
from 
    cteSimilar r
inner join 
    dbo.[ArticleDetail] a on r.ArticleDetailId = a.id
' 
END
GO
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'[dbo].[ArticleVectorData]') AND name = N'ArticleDetailsIdClusteredColumnStoreIndex')
CREATE NONCLUSTERED COLUMNSTORE INDEX [ArticleDetailsIdClusteredColumnStoreIndex] ON [dbo].[ArticleVectorData]
(
	[ArticleDetailId]
)WITH (DROP_EXISTING = OFF, COMPRESSION_DELAY = 0) ON [PRIMARY]
GO
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_ArticleDetail_Article]') AND parent_object_id = OBJECT_ID(N'[dbo].[ArticleDetail]'))
ALTER TABLE [dbo].[ArticleDetail]  WITH CHECK ADD  CONSTRAINT [FK_ArticleDetail_Article] FOREIGN KEY([ArticleId])
REFERENCES [dbo].[Article] ([Id])
ON DELETE CASCADE
GO
IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_ArticleDetail_Article]') AND parent_object_id = OBJECT_ID(N'[dbo].[ArticleDetail]'))
ALTER TABLE [dbo].[ArticleDetail] CHECK CONSTRAINT [FK_ArticleDetail_Article]
GO
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_ArticleVectorData_ArticleDetail]') AND parent_object_id = OBJECT_ID(N'[dbo].[ArticleVectorData]'))
ALTER TABLE [dbo].[ArticleVectorData]  WITH CHECK ADD  CONSTRAINT [FK_ArticleVectorData_ArticleDetail] FOREIGN KEY([ArticleDetailId])
REFERENCES [dbo].[ArticleDetail] ([Id])
ON DELETE CASCADE
GO
IF  EXISTS (SELECT * FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'[dbo].[FK_ArticleVectorData_ArticleDetail]') AND parent_object_id = OBJECT_ID(N'[dbo].[ArticleVectorData]'))
ALTER TABLE [dbo].[ArticleVectorData] CHECK CONSTRAINT [FK_ArticleVectorData_ArticleDetail]
GO              
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Version]') AND type in (N'U'))
BEGIN   
CREATE TABLE [dbo].[Version](   
    [Id] [int] IDENTITY(1,1) NOT NULL,
    [Type] [nvarchar](100) NOT NULL,
    [Version] [nvarchar](50) NOT NULL,
    CONSTRAINT [PK_Version] PRIMARY KEY CLUSTERED 
    (
        [Id] ASC
    )WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
END
GO
IF NOT EXISTS (SELECT * FROM [dbo].[Version] WHERE [Type] = 'SQL' AND [Version] = '1.00')
BEGIN
    INSERT INTO [dbo].[Version] ([Type], [Version]) VALUES ('SQL', '1.00');
END
GO
