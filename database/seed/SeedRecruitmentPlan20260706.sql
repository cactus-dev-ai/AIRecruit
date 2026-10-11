/*
  鑫可研发招聘计划初始化
  来源：鑫可人员招聘需求表研发250706.xls
  工作表：20260706
  源文件SHA-256：B8DC2EC4FB8FCAA1DBFA0B070513B7E8C02A6EC1D88BADDF765E3B452061D770
  兼容：SQL Server 2008 R2 / 数据库兼容级别100
  行为：幂等、仅补充缺失数据，不覆盖已有人工维护记录。
*/
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRY
  BEGIN TRANSACTION;

  DECLARE @AdminId BIGINT;
  SELECT TOP 1 @AdminId=u.Id
  FROM dbo.AppUser u INNER JOIN dbo.Role r ON r.Id=u.RoleId
  WHERE r.Code='ADMIN' AND u.Status='ACTIVE'
  ORDER BY u.Id;
  IF @AdminId IS NULL RAISERROR(N'缺少ACTIVE状态的ADMIN用户，无法初始化招聘计划。',16,1);


  DECLARE @Job1 BIGINT;
  SELECT TOP 1 @Job1=Id FROM dbo.Job WHERE Name=N'电驱总成集成设计工程师' AND IsDeleted=0 ORDER BY Id;
  IF @Job1 IS NULL
  BEGIN
    INSERT dbo.Job(Name,Description,Requirement,SkillRequirement,MinExperienceMonths,EducationRequirement,Status,CreatedBy,IsDeleted)
    VALUES(N'电驱总成集成设计工程师',N'招聘计划批次：20260706
计划人数：2人
薪资范围：15-50W
来源文件：鑫可人员招聘需求表研发250706.xls

工作内容：
1.电驱总成结构及其部件设计‌：负责电驱总成结构及其部件的设计、图纸绘制、尺寸链计算，确保设计符合技术要求和性能指标‌。
2.性能测试和供应商评估‌：负责电驱总成结构及其部件的性能测试、零部件认可、供应商评估，确保供应商提供的产品符合质量标准‌。
3.对外接口数据评审‌：负责电驱总成产品对外接口数据的评审，提供结构设计支持，确保产品接口的兼容性和稳定性‌。
4.技术研究和对标分析‌：负责电驱总成产品前言技术研究、专利分析、对标分析，持续优化产品设计和技术水平‌。
5.供应商管理和技术支持‌：参与供应商考察、技术评估及供应商质量提升，提供技术支持，确保供应商的产品和服务达到预期标准‌。
6.问题解决和技术文件编制‌：参与或负责设计开发、试装和试验过程中出现的问题，提供相应技术支持，编制相关技术文件‌。
7.测试和试验‌：负责电驱总成结构件设计、制造、装配、台架及整车试验问题的排查分析、汇报及闭环报告的编制‌。
8.‌基础技术研究：负责电驱总成结构件基础技术研究、对标分析、设计技术标准规范及专利的编制‌。',N'本科及以上，有电驱总成3年以上开发经验优先，
1.熟悉机械设计理论、3D和2D制图软件、机械部件材料选型依据、尺寸链分析等‌
2.熟悉动力总成设计流程、设计方法、工艺流程等‌
3.产品开发及管理的一般流程
4.有关整车匹配的相关知识
5.IATF16949相关知识',N'本科及以上，有电驱总成3年以上开发经验优先，
1.熟悉机械设计理论、3D和2D制图软件、机械部件材料选型依据、尺寸链分析等‌
2.熟悉动力总成设计流程、设计方法、工艺流程等‌
3.产品开发及管理的一般流程
4.有关整车匹配的相关知识
5.IATF16949相关知识',36,N'本科及以上','OPEN',@AdminId,0);
    SET @Job1=CAST(SCOPE_IDENTITY() AS BIGINT);
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.ScoreConfig WHERE JobId=@Job1)
    INSERT dbo.ScoreConfig(JobId,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,RuleJson,Version,UpdatedBy)
    VALUES(@Job1,40.00,30.00,20.00,10.00,N'{"schemaVersion":1,"source":{"file":"鑫可人员招聘需求表研发250706.xls","worksheet":"20260706","sha256":"B8DC2EC4FB8FCAA1DBFA0B070513B7E8C02A6EC1D88BADDF765E3B452061D770"},"recruitmentPlan":{"code":"XK-RD-20260706","sequence":1,"plannedHeadcount":2,"salaryRange":"15-50W"},"weights":{"skill":40,"project":30,"experience":20,"education":10}}',1,@AdminId);

  DECLARE @Doc1 BIGINT, @DocVersion1 INT;
  SELECT @Doc1=Id,@DocVersion1=Version FROM dbo.KnowledgeDocument WHERE Code=N'XK-RD-20260706-JOB-01';
  IF @Doc1 IS NULL
  BEGIN
    INSERT dbo.KnowledgeDocument(Code,Title,DocumentType,Content,Status,ContentHash,Version,CreatedBy,IsDeleted)
    VALUES(N'XK-RD-20260706-JOB-01',N'招聘计划：电驱总成集成设计工程师','JOB_REQUIREMENT',N'知识编码：XK-RD-20260706-JOB-01
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：电驱总成集成设计工程师
计划人数：2人
薪资范围：15-50W
最低工作经验：3年（36个月）
学历要求：本科及以上

岗位要求：
本科及以上，有电驱总成3年以上开发经验优先，
1.熟悉机械设计理论、3D和2D制图软件、机械部件材料选型依据、尺寸链分析等‌
2.熟悉动力总成设计流程、设计方法、工艺流程等‌
3.产品开发及管理的一般流程
4.有关整车匹配的相关知识
5.IATF16949相关知识

工作内容：
1.电驱总成结构及其部件设计‌：负责电驱总成结构及其部件的设计、图纸绘制、尺寸链计算，确保设计符合技术要求和性能指标‌。
2.性能测试和供应商评估‌：负责电驱总成结构及其部件的性能测试、零部件认可、供应商评估，确保供应商提供的产品符合质量标准‌。
3.对外接口数据评审‌：负责电驱总成产品对外接口数据的评审，提供结构设计支持，确保产品接口的兼容性和稳定性‌。
4.技术研究和对标分析‌：负责电驱总成产品前言技术研究、专利分析、对标分析，持续优化产品设计和技术水平‌。
5.供应商管理和技术支持‌：参与供应商考察、技术评估及供应商质量提升，提供技术支持，确保供应商的产品和服务达到预期标准‌。
6.问题解决和技术文件编制‌：参与或负责设计开发、试装和试验过程中出现的问题，提供相应技术支持，编制相关技术文件‌。
7.测试和试验‌：负责电驱总成结构件设计、制造、装配、台架及整车试验问题的排查分析、汇报及闭环报告的编制‌。
8.‌基础技术研究：负责电驱总成结构件基础技术研究、对标分析、设计技术标准规范及专利的编制‌。

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。','INDEXING','B04777C50353F4ED7010F48D766E64D4917FCE8AE3FD388E2615C25D07075797',1,@AdminId,0);
    SET @Doc1=CAST(SCOPE_IDENTITY() AS BIGINT); SET @DocVersion1=1;
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.KnowledgeChunk WHERE KnowledgeDocumentId=@Doc1 AND ChunkIndex=0)
    INSERT dbo.KnowledgeChunk(KnowledgeDocumentId,ChunkIndex,Content,TokenCount,MetadataJson,ContentHash)
    VALUES(@Doc1,0,N'知识编码：XK-RD-20260706-JOB-01
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：电驱总成集成设计工程师
计划人数：2人
薪资范围：15-50W
最低工作经验：3年（36个月）
学历要求：本科及以上

岗位要求：
本科及以上，有电驱总成3年以上开发经验优先，
1.熟悉机械设计理论、3D和2D制图软件、机械部件材料选型依据、尺寸链分析等‌
2.熟悉动力总成设计流程、设计方法、工艺流程等‌
3.产品开发及管理的一般流程
4.有关整车匹配的相关知识
5.IATF16949相关知识

工作内容：
1.电驱总成结构及其部件设计‌：负责电驱总成结构及其部件的设计、图纸绘制、尺寸链计算，确保设计符合技术要求和性能指标‌。
2.性能测试和供应商评估‌：负责电驱总成结构及其部件的性能测试、零部件认可、供应商评估，确保供应商提供的产品符合质量标准‌。
3.对外接口数据评审‌：负责电驱总成产品对外接口数据的评审，提供结构设计支持，确保产品接口的兼容性和稳定性‌。
4.技术研究和对标分析‌：负责电驱总成产品前言技术研究、专利分析、对标分析，持续优化产品设计和技术水平‌。
5.供应商管理和技术支持‌：参与供应商考察、技术评估及供应商质量提升，提供技术支持，确保供应商的产品和服务达到预期标准‌。
6.问题解决和技术文件编制‌：参与或负责设计开发、试装和试验过程中出现的问题，提供相应技术支持，编制相关技术文件‌。
7.测试和试验‌：负责电驱总成结构件设计、制造、装配、台架及整车试验问题的排查分析、汇报及闭环报告的编制‌。
8.‌基础技术研究：负责电驱总成结构件基础技术研究、对标分析、设计技术标准规范及专利的编制‌。

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。',NULL,N'{"sourceFile":"鑫可人员招聘需求表研发250706.xls","sourceWorksheet":"20260706","planCode":"XK-RD-20260706","sequence":1,"jobName":"电驱总成集成设计工程师","plannedHeadcount":2,"salaryRange":"15-50W"}','B04777C50353F4ED7010F48D766E64D4917FCE8AE3FD388E2615C25D07075797');
  DECLARE @TaskKey1 VARCHAR(200);
  SET @TaskKey1='knowledge-embed:'+CAST(@Doc1 AS VARCHAR(30))+':'+CAST(@DocVersion1 AS VARCHAR(20));
  IF NOT EXISTS (SELECT 1 FROM dbo.AITask WHERE BusinessKey=@TaskKey1)
    INSERT dbo.AITask(TaskType,KnowledgeDocumentId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey)
    VALUES('KNOWLEDGE_EMBED',@Doc1,'WAITING',5,0,3,@TaskKey1);


  DECLARE @Job2 BIGINT;
  SELECT TOP 1 @Job2=Id FROM dbo.Job WHERE Name=N'电机电磁设计工程师' AND IsDeleted=0 ORDER BY Id;
  IF @Job2 IS NULL
  BEGIN
    INSERT dbo.Job(Name,Description,Requirement,SkillRequirement,MinExperienceMonths,EducationRequirement,Status,CreatedBy,IsDeleted)
    VALUES(N'电机电磁设计工程师',N'招聘计划批次：20260706
计划人数：1人
薪资范围：20-45W
来源文件：鑫可人员招聘需求表研发250706.xls

工作内容：
1.完成电机的性能、内部结构设计、电磁计算、部件选型、设计图纸输出、实现、调试等；2.电机样机制作和跟进，电机异常分析及处理，相关测试及试验并报告；3.组织电磁方针分析及验证；4.配合产品工艺方案编制工艺文件及技术标准。',N'1.3年及以上车用驱动电机或伺服电机研发工程师岗位经验；熟悉电机结构和理论；2.熟悉电机类产品工艺及品质控制手段且具备驱动或伺服电机研发、电机电磁方案和结构设计能力，能独立完成电机本体开发及优化工作；3.熟练使用Ansoft、Ansys Maxwell等软件，熟练掌握绕组理论；4.熟练使用AutoCAD、SolidWorks、CAXA、CAD等绘制图纸。',N'1.3年及以上车用驱动电机或伺服电机研发工程师岗位经验；熟悉电机结构和理论；2.熟悉电机类产品工艺及品质控制手段且具备驱动或伺服电机研发、电机电磁方案和结构设计能力，能独立完成电机本体开发及优化工作；3.熟练使用Ansoft、Ansys Maxwell等软件，熟练掌握绕组理论；4.熟练使用AutoCAD、SolidWorks、CAXA、CAD等绘制图纸。',36,NULL,'OPEN',@AdminId,0);
    SET @Job2=CAST(SCOPE_IDENTITY() AS BIGINT);
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.ScoreConfig WHERE JobId=@Job2)
    INSERT dbo.ScoreConfig(JobId,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,RuleJson,Version,UpdatedBy)
    VALUES(@Job2,40.00,30.00,20.00,10.00,N'{"schemaVersion":1,"source":{"file":"鑫可人员招聘需求表研发250706.xls","worksheet":"20260706","sha256":"B8DC2EC4FB8FCAA1DBFA0B070513B7E8C02A6EC1D88BADDF765E3B452061D770"},"recruitmentPlan":{"code":"XK-RD-20260706","sequence":2,"plannedHeadcount":1,"salaryRange":"20-45W"},"weights":{"skill":40,"project":30,"experience":20,"education":10}}',1,@AdminId);

  DECLARE @Doc2 BIGINT, @DocVersion2 INT;
  SELECT @Doc2=Id,@DocVersion2=Version FROM dbo.KnowledgeDocument WHERE Code=N'XK-RD-20260706-JOB-02';
  IF @Doc2 IS NULL
  BEGIN
    INSERT dbo.KnowledgeDocument(Code,Title,DocumentType,Content,Status,ContentHash,Version,CreatedBy,IsDeleted)
    VALUES(N'XK-RD-20260706-JOB-02',N'招聘计划：电机电磁设计工程师','JOB_REQUIREMENT',N'知识编码：XK-RD-20260706-JOB-02
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：电机电磁设计工程师
计划人数：1人
薪资范围：20-45W
最低工作经验：3年（36个月）
学历要求：原计划未明确

岗位要求：
1.3年及以上车用驱动电机或伺服电机研发工程师岗位经验；熟悉电机结构和理论；2.熟悉电机类产品工艺及品质控制手段且具备驱动或伺服电机研发、电机电磁方案和结构设计能力，能独立完成电机本体开发及优化工作；3.熟练使用Ansoft、Ansys Maxwell等软件，熟练掌握绕组理论；4.熟练使用AutoCAD、SolidWorks、CAXA、CAD等绘制图纸。

工作内容：
1.完成电机的性能、内部结构设计、电磁计算、部件选型、设计图纸输出、实现、调试等；2.电机样机制作和跟进，电机异常分析及处理，相关测试及试验并报告；3.组织电磁方针分析及验证；4.配合产品工艺方案编制工艺文件及技术标准。

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。','INDEXING','9987DE7E122B5119C49946DD9D45AB7D457F01D94DCE78F1968E7202F0FC7FFC',1,@AdminId,0);
    SET @Doc2=CAST(SCOPE_IDENTITY() AS BIGINT); SET @DocVersion2=1;
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.KnowledgeChunk WHERE KnowledgeDocumentId=@Doc2 AND ChunkIndex=0)
    INSERT dbo.KnowledgeChunk(KnowledgeDocumentId,ChunkIndex,Content,TokenCount,MetadataJson,ContentHash)
    VALUES(@Doc2,0,N'知识编码：XK-RD-20260706-JOB-02
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：电机电磁设计工程师
计划人数：1人
薪资范围：20-45W
最低工作经验：3年（36个月）
学历要求：原计划未明确

岗位要求：
1.3年及以上车用驱动电机或伺服电机研发工程师岗位经验；熟悉电机结构和理论；2.熟悉电机类产品工艺及品质控制手段且具备驱动或伺服电机研发、电机电磁方案和结构设计能力，能独立完成电机本体开发及优化工作；3.熟练使用Ansoft、Ansys Maxwell等软件，熟练掌握绕组理论；4.熟练使用AutoCAD、SolidWorks、CAXA、CAD等绘制图纸。

工作内容：
1.完成电机的性能、内部结构设计、电磁计算、部件选型、设计图纸输出、实现、调试等；2.电机样机制作和跟进，电机异常分析及处理，相关测试及试验并报告；3.组织电磁方针分析及验证；4.配合产品工艺方案编制工艺文件及技术标准。

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。',NULL,N'{"sourceFile":"鑫可人员招聘需求表研发250706.xls","sourceWorksheet":"20260706","planCode":"XK-RD-20260706","sequence":2,"jobName":"电机电磁设计工程师","plannedHeadcount":1,"salaryRange":"20-45W"}','9987DE7E122B5119C49946DD9D45AB7D457F01D94DCE78F1968E7202F0FC7FFC');
  DECLARE @TaskKey2 VARCHAR(200);
  SET @TaskKey2='knowledge-embed:'+CAST(@Doc2 AS VARCHAR(30))+':'+CAST(@DocVersion2 AS VARCHAR(20));
  IF NOT EXISTS (SELECT 1 FROM dbo.AITask WHERE BusinessKey=@TaskKey2)
    INSERT dbo.AITask(TaskType,KnowledgeDocumentId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey)
    VALUES('KNOWLEDGE_EMBED',@Doc2,'WAITING',5,0,3,@TaskKey2);


  DECLARE @Job3 BIGINT;
  SELECT TOP 1 @Job3=Id FROM dbo.Job WHERE Name=N'电机结构工程师' AND IsDeleted=0 ORDER BY Id;
  IF @Job3 IS NULL
  BEGIN
    INSERT dbo.Job(Name,Description,Requirement,SkillRequirement,MinExperienceMonths,EducationRequirement,Status,CreatedBy,IsDeleted)
    VALUES(N'电机结构工程师',N'招聘计划批次：20260706
计划人数：3人
薪资范围：15-35W
来源文件：鑫可人员招聘需求表研发250706.xls

工作内容：
1.负责电机的机械结构设计和相关总成、零部件及材料规范 ；2.负责电机机械及电气相关接口设计，协同电驱总成开展系统匹配及集成；3.负责电机绝缘系统设计、绝缘材料的选型及应用；4.完成电机及关联子零件的技术文档(数模、图纸、订货规范等);5.协调公司内部相关接口部门(采购、工厂等),完成样品生产；6.在产品试制、试验及生产阶段，针对发生的问题提供技术支持，参与相关问题分析解决。',N'1.本科，机械制造及自动化、车辆工程、电机等专业；3年以上国内外大型零部件企业驱动电机机械开发工作经验优先；2.具有较强的机械设计和计算能力，熟悉金属件和塑料件的设计方法和加工工艺，具备扁线电机、油冷电机、高速电机开发经验者优先；3.熟练使用UG,Catia,MS office等软件；4.勤奋踏实，能够适应高强度的工作，抗压能力强，良好的沟通，团队合作及英语水平，有五年电机结构开发相关工作经验可放宽要求。',N'1.本科，机械制造及自动化、车辆工程、电机等专业；3年以上国内外大型零部件企业驱动电机机械开发工作经验优先；2.具有较强的机械设计和计算能力，熟悉金属件和塑料件的设计方法和加工工艺，具备扁线电机、油冷电机、高速电机开发经验者优先；3.熟练使用UG,Catia,MS office等软件；4.勤奋踏实，能够适应高强度的工作，抗压能力强，良好的沟通，团队合作及英语水平，有五年电机结构开发相关工作经验可放宽要求。',36,N'本科','OPEN',@AdminId,0);
    SET @Job3=CAST(SCOPE_IDENTITY() AS BIGINT);
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.ScoreConfig WHERE JobId=@Job3)
    INSERT dbo.ScoreConfig(JobId,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,RuleJson,Version,UpdatedBy)
    VALUES(@Job3,40.00,30.00,20.00,10.00,N'{"schemaVersion":1,"source":{"file":"鑫可人员招聘需求表研发250706.xls","worksheet":"20260706","sha256":"B8DC2EC4FB8FCAA1DBFA0B070513B7E8C02A6EC1D88BADDF765E3B452061D770"},"recruitmentPlan":{"code":"XK-RD-20260706","sequence":3,"plannedHeadcount":3,"salaryRange":"15-35W"},"weights":{"skill":40,"project":30,"experience":20,"education":10}}',1,@AdminId);

  DECLARE @Doc3 BIGINT, @DocVersion3 INT;
  SELECT @Doc3=Id,@DocVersion3=Version FROM dbo.KnowledgeDocument WHERE Code=N'XK-RD-20260706-JOB-03';
  IF @Doc3 IS NULL
  BEGIN
    INSERT dbo.KnowledgeDocument(Code,Title,DocumentType,Content,Status,ContentHash,Version,CreatedBy,IsDeleted)
    VALUES(N'XK-RD-20260706-JOB-03',N'招聘计划：电机结构工程师','JOB_REQUIREMENT',N'知识编码：XK-RD-20260706-JOB-03
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：电机结构工程师
计划人数：3人
薪资范围：15-35W
最低工作经验：3年（36个月）
学历要求：本科

岗位要求：
1.本科，机械制造及自动化、车辆工程、电机等专业；3年以上国内外大型零部件企业驱动电机机械开发工作经验优先；2.具有较强的机械设计和计算能力，熟悉金属件和塑料件的设计方法和加工工艺，具备扁线电机、油冷电机、高速电机开发经验者优先；3.熟练使用UG,Catia,MS office等软件；4.勤奋踏实，能够适应高强度的工作，抗压能力强，良好的沟通，团队合作及英语水平，有五年电机结构开发相关工作经验可放宽要求。

工作内容：
1.负责电机的机械结构设计和相关总成、零部件及材料规范 ；2.负责电机机械及电气相关接口设计，协同电驱总成开展系统匹配及集成；3.负责电机绝缘系统设计、绝缘材料的选型及应用；4.完成电机及关联子零件的技术文档(数模、图纸、订货规范等);5.协调公司内部相关接口部门(采购、工厂等),完成样品生产；6.在产品试制、试验及生产阶段，针对发生的问题提供技术支持，参与相关问题分析解决。

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。','INDEXING','37DA956E63D2C51C1885046A61F6C5544FE031973FFF6AA351ACADDB9D704D79',1,@AdminId,0);
    SET @Doc3=CAST(SCOPE_IDENTITY() AS BIGINT); SET @DocVersion3=1;
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.KnowledgeChunk WHERE KnowledgeDocumentId=@Doc3 AND ChunkIndex=0)
    INSERT dbo.KnowledgeChunk(KnowledgeDocumentId,ChunkIndex,Content,TokenCount,MetadataJson,ContentHash)
    VALUES(@Doc3,0,N'知识编码：XK-RD-20260706-JOB-03
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：电机结构工程师
计划人数：3人
薪资范围：15-35W
最低工作经验：3年（36个月）
学历要求：本科

岗位要求：
1.本科，机械制造及自动化、车辆工程、电机等专业；3年以上国内外大型零部件企业驱动电机机械开发工作经验优先；2.具有较强的机械设计和计算能力，熟悉金属件和塑料件的设计方法和加工工艺，具备扁线电机、油冷电机、高速电机开发经验者优先；3.熟练使用UG,Catia,MS office等软件；4.勤奋踏实，能够适应高强度的工作，抗压能力强，良好的沟通，团队合作及英语水平，有五年电机结构开发相关工作经验可放宽要求。

工作内容：
1.负责电机的机械结构设计和相关总成、零部件及材料规范 ；2.负责电机机械及电气相关接口设计，协同电驱总成开展系统匹配及集成；3.负责电机绝缘系统设计、绝缘材料的选型及应用；4.完成电机及关联子零件的技术文档(数模、图纸、订货规范等);5.协调公司内部相关接口部门(采购、工厂等),完成样品生产；6.在产品试制、试验及生产阶段，针对发生的问题提供技术支持，参与相关问题分析解决。

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。',NULL,N'{"sourceFile":"鑫可人员招聘需求表研发250706.xls","sourceWorksheet":"20260706","planCode":"XK-RD-20260706","sequence":3,"jobName":"电机结构工程师","plannedHeadcount":3,"salaryRange":"15-35W"}','37DA956E63D2C51C1885046A61F6C5544FE031973FFF6AA351ACADDB9D704D79');
  DECLARE @TaskKey3 VARCHAR(200);
  SET @TaskKey3='knowledge-embed:'+CAST(@Doc3 AS VARCHAR(30))+':'+CAST(@DocVersion3 AS VARCHAR(20));
  IF NOT EXISTS (SELECT 1 FROM dbo.AITask WHERE BusinessKey=@TaskKey3)
    INSERT dbo.AITask(TaskType,KnowledgeDocumentId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey)
    VALUES('KNOWLEDGE_EMBED',@Doc3,'WAITING',5,0,3,@TaskKey3);


  DECLARE @Job4 BIGINT;
  SELECT TOP 1 @Job4=Id FROM dbo.Job WHERE Name=N'传动换挡机构设计工程师' AND IsDeleted=0 ORDER BY Id;
  IF @Job4 IS NULL
  BEGIN
    INSERT dbo.Job(Name,Description,Requirement,SkillRequirement,MinExperienceMonths,EducationRequirement,Status,CreatedBy,IsDeleted)
    VALUES(N'传动换挡机构设计工程师',N'招聘计划批次：20260706
计划人数：1人
薪资范围：10-25W
来源文件：鑫可人员招聘需求表研发250706.xls

工作内容：
1.传动系统方案设计‌：负责电驱系统传动系统的方案设计及策划，包括电机轴、轴承、齿轮、主减速器等传动部品的性能达成和匹配设计开发工作‌。
2.性能优化和测试‌：负责电机与减速器/变速器系统动力匹配与性能优化，进行传动系统元件的相关性能测试，并编制相关技术文件‌。
‌3.设计校核和计算分析‌：进行传动系方案的布置、设计校核及计算分析，协助试验大纲编制及试制、试验问题解决，市场问题排查及解决，标杆机型分析及对标研究‌。
‌4.技术文件编制‌：负责机械设计文件的编制及校对，参与机械部分的设计优化工作，配合完成机械部分的装配和调试工作，对所设计的电驱传动系统进行技术交流和技术改进工作‌。
‌5.技术支持‌：在制造和售后过程中，提供传动系统相关技术支持‌。
‌6.成本分析和预算‌：协助传动系统和部品设计开发开发费、部品成本分析和预算目标达成、进度会资料总结汇报等‌。
‌7.技术发展跟踪‌：跟踪传动系统技术发展动向，推动本领域的先进技术开发，了解国内外市场客户需求，提供相关技术支持‌。
8.负责组织减/变速器总成及其他传动部件基础技术研究、对标分析、设计技术标准规范及专利的编制‌或制定。
9.负责传动设计模块组的相关工作。',N'本科，3年以上相关工作经验，有电驱或减速器总成开发经验优先，1.熟悉机械设计理论、3D和2D制图软件、机械部件材料选型依据、尺寸链分析等‌
2.熟悉动力总成设计流程、设计方法、工艺流程等‌
3.熟悉减速器/变速器建模软件，如MAST等4.产品开发及管理的一般流程
4.掌握和应用有关整车匹配的相关知识
5.掌握和应用IATF16949相关知识',N'本科，3年以上相关工作经验，有电驱或减速器总成开发经验优先，1.熟悉机械设计理论、3D和2D制图软件、机械部件材料选型依据、尺寸链分析等‌
2.熟悉动力总成设计流程、设计方法、工艺流程等‌
3.熟悉减速器/变速器建模软件，如MAST等4.产品开发及管理的一般流程
4.掌握和应用有关整车匹配的相关知识
5.掌握和应用IATF16949相关知识',36,N'本科','OPEN',@AdminId,0);
    SET @Job4=CAST(SCOPE_IDENTITY() AS BIGINT);
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.ScoreConfig WHERE JobId=@Job4)
    INSERT dbo.ScoreConfig(JobId,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,RuleJson,Version,UpdatedBy)
    VALUES(@Job4,40.00,30.00,20.00,10.00,N'{"schemaVersion":1,"source":{"file":"鑫可人员招聘需求表研发250706.xls","worksheet":"20260706","sha256":"B8DC2EC4FB8FCAA1DBFA0B070513B7E8C02A6EC1D88BADDF765E3B452061D770"},"recruitmentPlan":{"code":"XK-RD-20260706","sequence":4,"plannedHeadcount":1,"salaryRange":"10-25W"},"weights":{"skill":40,"project":30,"experience":20,"education":10}}',1,@AdminId);

  DECLARE @Doc4 BIGINT, @DocVersion4 INT;
  SELECT @Doc4=Id,@DocVersion4=Version FROM dbo.KnowledgeDocument WHERE Code=N'XK-RD-20260706-JOB-04';
  IF @Doc4 IS NULL
  BEGIN
    INSERT dbo.KnowledgeDocument(Code,Title,DocumentType,Content,Status,ContentHash,Version,CreatedBy,IsDeleted)
    VALUES(N'XK-RD-20260706-JOB-04',N'招聘计划：传动换挡机构设计工程师','JOB_REQUIREMENT',N'知识编码：XK-RD-20260706-JOB-04
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：传动换挡机构设计工程师
计划人数：1人
薪资范围：10-25W
最低工作经验：3年（36个月）
学历要求：本科

岗位要求：
本科，3年以上相关工作经验，有电驱或减速器总成开发经验优先，1.熟悉机械设计理论、3D和2D制图软件、机械部件材料选型依据、尺寸链分析等‌
2.熟悉动力总成设计流程、设计方法、工艺流程等‌
3.熟悉减速器/变速器建模软件，如MAST等4.产品开发及管理的一般流程
4.掌握和应用有关整车匹配的相关知识
5.掌握和应用IATF16949相关知识

工作内容：
1.传动系统方案设计‌：负责电驱系统传动系统的方案设计及策划，包括电机轴、轴承、齿轮、主减速器等传动部品的性能达成和匹配设计开发工作‌。
2.性能优化和测试‌：负责电机与减速器/变速器系统动力匹配与性能优化，进行传动系统元件的相关性能测试，并编制相关技术文件‌。
‌3.设计校核和计算分析‌：进行传动系方案的布置、设计校核及计算分析，协助试验大纲编制及试制、试验问题解决，市场问题排查及解决，标杆机型分析及对标研究‌。
‌4.技术文件编制‌：负责机械设计文件的编制及校对，参与机械部分的设计优化工作，配合完成机械部分的装配和调试工作，对所设计的电驱传动系统进行技术交流和技术改进工作‌。
‌5.技术支持‌：在制造和售后过程中，提供传动系统相关技术支持‌。
‌6.成本分析和预算‌：协助传动系统和部品设计开发开发费、部品成本分析和预算目标达成、进度会资料总结汇报等‌。
‌7.技术发展跟踪‌：跟踪传动系统技术发展动向，推动本领域的先进技术开发，了解国内外市场客户需求，提供相关技术支持‌。
8.负责组织减/变速器总成及其他传动部件基础技术研究、对标分析、设计技术标准规范及专利的编制‌或制定。
9.负责传动设计模块组的相关工作。

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。','INDEXING','C492C76A64DFC6C054EF6970D326871271F7144B55BC870E884DB3F6D7715C7C',1,@AdminId,0);
    SET @Doc4=CAST(SCOPE_IDENTITY() AS BIGINT); SET @DocVersion4=1;
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.KnowledgeChunk WHERE KnowledgeDocumentId=@Doc4 AND ChunkIndex=0)
    INSERT dbo.KnowledgeChunk(KnowledgeDocumentId,ChunkIndex,Content,TokenCount,MetadataJson,ContentHash)
    VALUES(@Doc4,0,N'知识编码：XK-RD-20260706-JOB-04
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：传动换挡机构设计工程师
计划人数：1人
薪资范围：10-25W
最低工作经验：3年（36个月）
学历要求：本科

岗位要求：
本科，3年以上相关工作经验，有电驱或减速器总成开发经验优先，1.熟悉机械设计理论、3D和2D制图软件、机械部件材料选型依据、尺寸链分析等‌
2.熟悉动力总成设计流程、设计方法、工艺流程等‌
3.熟悉减速器/变速器建模软件，如MAST等4.产品开发及管理的一般流程
4.掌握和应用有关整车匹配的相关知识
5.掌握和应用IATF16949相关知识

工作内容：
1.传动系统方案设计‌：负责电驱系统传动系统的方案设计及策划，包括电机轴、轴承、齿轮、主减速器等传动部品的性能达成和匹配设计开发工作‌。
2.性能优化和测试‌：负责电机与减速器/变速器系统动力匹配与性能优化，进行传动系统元件的相关性能测试，并编制相关技术文件‌。
‌3.设计校核和计算分析‌：进行传动系方案的布置、设计校核及计算分析，协助试验大纲编制及试制、试验问题解决，市场问题排查及解决，标杆机型分析及对标研究‌。
‌4.技术文件编制‌：负责机械设计文件的编制及校对，参与机械部分的设计优化工作，配合完成机械部分的装配和调试工作，对所设计的电驱传动系统进行技术交流和技术改进工作‌。
‌5.技术支持‌：在制造和售后过程中，提供传动系统相关技术支持‌。
‌6.成本分析和预算‌：协助传动系统和部品设计开发开发费、部品成本分析和预算目标达成、进度会资料总结汇报等‌。
‌7.技术发展跟踪‌：跟踪传动系统技术发展动向，推动本领域的先进技术开发，了解国内外市场客户需求，提供相关技术支持‌。
8.负责组织减/变速器总成及其他传动部件基础技术研究、对标分析、设计技术标准规范及专利的编制‌或制定。
9.负责传动设计模块组的相关工作。

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。',NULL,N'{"sourceFile":"鑫可人员招聘需求表研发250706.xls","sourceWorksheet":"20260706","planCode":"XK-RD-20260706","sequence":4,"jobName":"传动换挡机构设计工程师","plannedHeadcount":1,"salaryRange":"10-25W"}','C492C76A64DFC6C054EF6970D326871271F7144B55BC870E884DB3F6D7715C7C');
  DECLARE @TaskKey4 VARCHAR(200);
  SET @TaskKey4='knowledge-embed:'+CAST(@Doc4 AS VARCHAR(30))+':'+CAST(@DocVersion4 AS VARCHAR(20));
  IF NOT EXISTS (SELECT 1 FROM dbo.AITask WHERE BusinessKey=@TaskKey4)
    INSERT dbo.AITask(TaskType,KnowledgeDocumentId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey)
    VALUES('KNOWLEDGE_EMBED',@Doc4,'WAITING',5,0,3,@TaskKey4);


  DECLARE @Job5 BIGINT;
  SELECT TOP 1 @Job5=Id FROM dbo.Job WHERE Name=N'结构设计工程师' AND IsDeleted=0 ORDER BY Id;
  IF @Job5 IS NULL
  BEGIN
    INSERT dbo.Job(Name,Description,Requirement,SkillRequirement,MinExperienceMonths,EducationRequirement,Status,CreatedBy,IsDeleted)
    VALUES(N'结构设计工程师',N'招聘计划批次：20260706
计划人数：3人
薪资范围：10-20W
来源文件：鑫可人员招聘需求表研发250706.xls

工作内容：
1.结构‌设计：负责设计外壳、选换挡件等结构件的设计及计算分析工作，包括壳体类、换挡类、油管类、轴承压板类等零部件的设计、建模及二维图纸绘制，编制相关技术文档等‌。
‌2.优化和改进设计‌：根据设计空间及总布置的要求，设计壳体结构及细节，编制壳体周边尺寸链报告，并根据CAE分析结果优化壳体‌等结构件。
‌3.技术支持与指导‌：对产品的结构设计过程提供技术支持与指导，参与产品开发中的问题分析和解决，提出合理的优化建议‌。
‌4.规范和标准制定‌：根据产品设计要求编制相关规范和技术文件，确保壳体等结构件设计和制造符合产品规格要求‌。
‌5.检测和验证‌：协助产品生产后的检测和检验，编写有关技术资料，并对产品的结构优化提出改进意见并实施验证‌。
‌6.参与评审和试验‌：参与壳体等结构件DFMEA评审，按项目要求参与竞品分析、样箱装配及试验验证分析。
7.模具开发：参与供方壳体等结构件评审，协助工艺部门进行自主模具设计、制作等。
8.负责电驱系统中电机、电控、减速器/变速器结构件基础技术研究、对标分析、设计技术标准规范及专利的编制‌或制定。',N'本科，3年以上工作经验有电驱或减速器总成开发经验者优先，1.熟悉机械设计理论、3D和2D制图软件、机械部件材料选型依据、尺寸链分析等‌
2.熟悉动力总成设计流程、设计方法、工艺流程等‌产品开发及管理的一般流程
3.掌握和应用有关整车匹配的相关知识
4.掌握和应用IATF16949相关知识',N'本科，3年以上工作经验有电驱或减速器总成开发经验者优先，1.熟悉机械设计理论、3D和2D制图软件、机械部件材料选型依据、尺寸链分析等‌
2.熟悉动力总成设计流程、设计方法、工艺流程等‌产品开发及管理的一般流程
3.掌握和应用有关整车匹配的相关知识
4.掌握和应用IATF16949相关知识',36,N'本科','OPEN',@AdminId,0);
    SET @Job5=CAST(SCOPE_IDENTITY() AS BIGINT);
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.ScoreConfig WHERE JobId=@Job5)
    INSERT dbo.ScoreConfig(JobId,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,RuleJson,Version,UpdatedBy)
    VALUES(@Job5,40.00,30.00,20.00,10.00,N'{"schemaVersion":1,"source":{"file":"鑫可人员招聘需求表研发250706.xls","worksheet":"20260706","sha256":"B8DC2EC4FB8FCAA1DBFA0B070513B7E8C02A6EC1D88BADDF765E3B452061D770"},"recruitmentPlan":{"code":"XK-RD-20260706","sequence":5,"plannedHeadcount":3,"salaryRange":"10-20W"},"weights":{"skill":40,"project":30,"experience":20,"education":10}}',1,@AdminId);

  DECLARE @Doc5 BIGINT, @DocVersion5 INT;
  SELECT @Doc5=Id,@DocVersion5=Version FROM dbo.KnowledgeDocument WHERE Code=N'XK-RD-20260706-JOB-05';
  IF @Doc5 IS NULL
  BEGIN
    INSERT dbo.KnowledgeDocument(Code,Title,DocumentType,Content,Status,ContentHash,Version,CreatedBy,IsDeleted)
    VALUES(N'XK-RD-20260706-JOB-05',N'招聘计划：结构设计工程师','JOB_REQUIREMENT',N'知识编码：XK-RD-20260706-JOB-05
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：结构设计工程师
计划人数：3人
薪资范围：10-20W
最低工作经验：3年（36个月）
学历要求：本科

岗位要求：
本科，3年以上工作经验有电驱或减速器总成开发经验者优先，1.熟悉机械设计理论、3D和2D制图软件、机械部件材料选型依据、尺寸链分析等‌
2.熟悉动力总成设计流程、设计方法、工艺流程等‌产品开发及管理的一般流程
3.掌握和应用有关整车匹配的相关知识
4.掌握和应用IATF16949相关知识

工作内容：
1.结构‌设计：负责设计外壳、选换挡件等结构件的设计及计算分析工作，包括壳体类、换挡类、油管类、轴承压板类等零部件的设计、建模及二维图纸绘制，编制相关技术文档等‌。
‌2.优化和改进设计‌：根据设计空间及总布置的要求，设计壳体结构及细节，编制壳体周边尺寸链报告，并根据CAE分析结果优化壳体‌等结构件。
‌3.技术支持与指导‌：对产品的结构设计过程提供技术支持与指导，参与产品开发中的问题分析和解决，提出合理的优化建议‌。
‌4.规范和标准制定‌：根据产品设计要求编制相关规范和技术文件，确保壳体等结构件设计和制造符合产品规格要求‌。
‌5.检测和验证‌：协助产品生产后的检测和检验，编写有关技术资料，并对产品的结构优化提出改进意见并实施验证‌。
‌6.参与评审和试验‌：参与壳体等结构件DFMEA评审，按项目要求参与竞品分析、样箱装配及试验验证分析。
7.模具开发：参与供方壳体等结构件评审，协助工艺部门进行自主模具设计、制作等。
8.负责电驱系统中电机、电控、减速器/变速器结构件基础技术研究、对标分析、设计技术标准规范及专利的编制‌或制定。

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。','INDEXING','A6126F9510258DE32FFBCE7A81BAE5FDB468C0B212074B8FA88A5E4751562909',1,@AdminId,0);
    SET @Doc5=CAST(SCOPE_IDENTITY() AS BIGINT); SET @DocVersion5=1;
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.KnowledgeChunk WHERE KnowledgeDocumentId=@Doc5 AND ChunkIndex=0)
    INSERT dbo.KnowledgeChunk(KnowledgeDocumentId,ChunkIndex,Content,TokenCount,MetadataJson,ContentHash)
    VALUES(@Doc5,0,N'知识编码：XK-RD-20260706-JOB-05
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：结构设计工程师
计划人数：3人
薪资范围：10-20W
最低工作经验：3年（36个月）
学历要求：本科

岗位要求：
本科，3年以上工作经验有电驱或减速器总成开发经验者优先，1.熟悉机械设计理论、3D和2D制图软件、机械部件材料选型依据、尺寸链分析等‌
2.熟悉动力总成设计流程、设计方法、工艺流程等‌产品开发及管理的一般流程
3.掌握和应用有关整车匹配的相关知识
4.掌握和应用IATF16949相关知识

工作内容：
1.结构‌设计：负责设计外壳、选换挡件等结构件的设计及计算分析工作，包括壳体类、换挡类、油管类、轴承压板类等零部件的设计、建模及二维图纸绘制，编制相关技术文档等‌。
‌2.优化和改进设计‌：根据设计空间及总布置的要求，设计壳体结构及细节，编制壳体周边尺寸链报告，并根据CAE分析结果优化壳体‌等结构件。
‌3.技术支持与指导‌：对产品的结构设计过程提供技术支持与指导，参与产品开发中的问题分析和解决，提出合理的优化建议‌。
‌4.规范和标准制定‌：根据产品设计要求编制相关规范和技术文件，确保壳体等结构件设计和制造符合产品规格要求‌。
‌5.检测和验证‌：协助产品生产后的检测和检验，编写有关技术资料，并对产品的结构优化提出改进意见并实施验证‌。
‌6.参与评审和试验‌：参与壳体等结构件DFMEA评审，按项目要求参与竞品分析、样箱装配及试验验证分析。
7.模具开发：参与供方壳体等结构件评审，协助工艺部门进行自主模具设计、制作等。
8.负责电驱系统中电机、电控、减速器/变速器结构件基础技术研究、对标分析、设计技术标准规范及专利的编制‌或制定。

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。',NULL,N'{"sourceFile":"鑫可人员招聘需求表研发250706.xls","sourceWorksheet":"20260706","planCode":"XK-RD-20260706","sequence":5,"jobName":"结构设计工程师","plannedHeadcount":3,"salaryRange":"10-20W"}','A6126F9510258DE32FFBCE7A81BAE5FDB468C0B212074B8FA88A5E4751562909');
  DECLARE @TaskKey5 VARCHAR(200);
  SET @TaskKey5='knowledge-embed:'+CAST(@Doc5 AS VARCHAR(30))+':'+CAST(@DocVersion5 AS VARCHAR(20));
  IF NOT EXISTS (SELECT 1 FROM dbo.AITask WHERE BusinessKey=@TaskKey5)
    INSERT dbo.AITask(TaskType,KnowledgeDocumentId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey)
    VALUES('KNOWLEDGE_EMBED',@Doc5,'WAITING',5,0,3,@TaskKey5);


  DECLARE @Job6 BIGINT;
  SELECT TOP 1 @Job6=Id FROM dbo.Job WHERE Name=N'CAE工程师' AND IsDeleted=0 ORDER BY Id;
  IF @Job6 IS NULL
  BEGIN
    INSERT dbo.Job(Name,Description,Requirement,SkillRequirement,MinExperienceMonths,EducationRequirement,Status,CreatedBy,IsDeleted)
    VALUES(N'CAE工程师',N'招聘计划批次：20260706
计划人数：1人
薪资范围：12-30W
来源文件：鑫可人员招聘需求表研发250706.xls

工作内容：
1.进行仿真分析‌：负责对相关产品进行强度疲劳、结构优化、NVH或流体换热噪声等仿真分析‌。
‌2.评估分析结果‌：对分析结果进行评估，形成清晰易懂的报告‌。
‌3.优化设计方案‌：与设计工程师沟通，给出合理的优化设计方案‌。
‌4.仿真与试验对标‌：负责仿真与试验对标，修正分析方法，提升对标精度，总结经验‌。
‌5.跟踪技术发展‌：跟踪和研究国内外CAE仿真分析技术的发展动向‌，编制CAE分析技术条件。
‌6.技术支持：根据软件需求分析，提供设计思路和建议；与测试人员配合完成功能验证及系统优化工作；参与产品规划阶段的技术方案讨论，并提供技术支持意见。
‌7.技术文档和报告‌：负责编写三维模型的几何约束分析、技术报告、技术汇报PPT、软件说明书、软件使用手册、软件理论手册、软件测试手册等‌。
‌8.失效分析和建模‌：负责电驱系统部件等机械系统失效分析建模与分析工作；对产品的设计提出合理的改进建议；协助完成项目的技术文档编写及整理工作；协助解决技术问题和难题；参与产品可靠性评估‌。
9.负责CAE仿真基础技术研究、对标分析、设计技术标准规范及专利的编制‌或制定。',N'本科，3年以上相关经验，有强度疲劳、结构优化、NVH或流体换热噪声等仿真分析经开发经验优先，1.产品开发及管理的一般流程
2.掌握和应用有关整车匹配的相关知识
3.掌握和应用IATF16949相关知识
4.熟练掌握相关建模和仿真分析工具，如ANSA、ABAQUS、SIGHT、TOSCA、FE-SAFE等；能够独立完成客户相关结构产品静力分析、线性动力学分析、显式动力学分析、多场耦合分析、无参优化分析、参数优化分析、疲劳分析等‌',N'本科，3年以上相关经验，有强度疲劳、结构优化、NVH或流体换热噪声等仿真分析经开发经验优先，1.产品开发及管理的一般流程
2.掌握和应用有关整车匹配的相关知识
3.掌握和应用IATF16949相关知识
4.熟练掌握相关建模和仿真分析工具，如ANSA、ABAQUS、SIGHT、TOSCA、FE-SAFE等；能够独立完成客户相关结构产品静力分析、线性动力学分析、显式动力学分析、多场耦合分析、无参优化分析、参数优化分析、疲劳分析等‌',36,N'本科','OPEN',@AdminId,0);
    SET @Job6=CAST(SCOPE_IDENTITY() AS BIGINT);
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.ScoreConfig WHERE JobId=@Job6)
    INSERT dbo.ScoreConfig(JobId,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,RuleJson,Version,UpdatedBy)
    VALUES(@Job6,40.00,30.00,20.00,10.00,N'{"schemaVersion":1,"source":{"file":"鑫可人员招聘需求表研发250706.xls","worksheet":"20260706","sha256":"B8DC2EC4FB8FCAA1DBFA0B070513B7E8C02A6EC1D88BADDF765E3B452061D770"},"recruitmentPlan":{"code":"XK-RD-20260706","sequence":6,"plannedHeadcount":1,"salaryRange":"12-30W"},"weights":{"skill":40,"project":30,"experience":20,"education":10}}',1,@AdminId);

  DECLARE @Doc6 BIGINT, @DocVersion6 INT;
  SELECT @Doc6=Id,@DocVersion6=Version FROM dbo.KnowledgeDocument WHERE Code=N'XK-RD-20260706-JOB-06';
  IF @Doc6 IS NULL
  BEGIN
    INSERT dbo.KnowledgeDocument(Code,Title,DocumentType,Content,Status,ContentHash,Version,CreatedBy,IsDeleted)
    VALUES(N'XK-RD-20260706-JOB-06',N'招聘计划：CAE工程师','JOB_REQUIREMENT',N'知识编码：XK-RD-20260706-JOB-06
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：CAE工程师
计划人数：1人
薪资范围：12-30W
最低工作经验：3年（36个月）
学历要求：本科

岗位要求：
本科，3年以上相关经验，有强度疲劳、结构优化、NVH或流体换热噪声等仿真分析经开发经验优先，1.产品开发及管理的一般流程
2.掌握和应用有关整车匹配的相关知识
3.掌握和应用IATF16949相关知识
4.熟练掌握相关建模和仿真分析工具，如ANSA、ABAQUS、SIGHT、TOSCA、FE-SAFE等；能够独立完成客户相关结构产品静力分析、线性动力学分析、显式动力学分析、多场耦合分析、无参优化分析、参数优化分析、疲劳分析等‌

工作内容：
1.进行仿真分析‌：负责对相关产品进行强度疲劳、结构优化、NVH或流体换热噪声等仿真分析‌。
‌2.评估分析结果‌：对分析结果进行评估，形成清晰易懂的报告‌。
‌3.优化设计方案‌：与设计工程师沟通，给出合理的优化设计方案‌。
‌4.仿真与试验对标‌：负责仿真与试验对标，修正分析方法，提升对标精度，总结经验‌。
‌5.跟踪技术发展‌：跟踪和研究国内外CAE仿真分析技术的发展动向‌，编制CAE分析技术条件。
‌6.技术支持：根据软件需求分析，提供设计思路和建议；与测试人员配合完成功能验证及系统优化工作；参与产品规划阶段的技术方案讨论，并提供技术支持意见。
‌7.技术文档和报告‌：负责编写三维模型的几何约束分析、技术报告、技术汇报PPT、软件说明书、软件使用手册、软件理论手册、软件测试手册等‌。
‌8.失效分析和建模‌：负责电驱系统部件等机械系统失效分析建模与分析工作；对产品的设计提出合理的改进建议；协助完成项目的技术文档编写及整理工作；协助解决技术问题和难题；参与产品可靠性评估‌。
9.负责CAE仿真基础技术研究、对标分析、设计技术标准规范及专利的编制‌或制定。

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。','INDEXING','010499B91C3F3DE4A6F20B0257406DCBD262BD7DADF27FE2AD8319EA01B998B8',1,@AdminId,0);
    SET @Doc6=CAST(SCOPE_IDENTITY() AS BIGINT); SET @DocVersion6=1;
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.KnowledgeChunk WHERE KnowledgeDocumentId=@Doc6 AND ChunkIndex=0)
    INSERT dbo.KnowledgeChunk(KnowledgeDocumentId,ChunkIndex,Content,TokenCount,MetadataJson,ContentHash)
    VALUES(@Doc6,0,N'知识编码：XK-RD-20260706-JOB-06
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：CAE工程师
计划人数：1人
薪资范围：12-30W
最低工作经验：3年（36个月）
学历要求：本科

岗位要求：
本科，3年以上相关经验，有强度疲劳、结构优化、NVH或流体换热噪声等仿真分析经开发经验优先，1.产品开发及管理的一般流程
2.掌握和应用有关整车匹配的相关知识
3.掌握和应用IATF16949相关知识
4.熟练掌握相关建模和仿真分析工具，如ANSA、ABAQUS、SIGHT、TOSCA、FE-SAFE等；能够独立完成客户相关结构产品静力分析、线性动力学分析、显式动力学分析、多场耦合分析、无参优化分析、参数优化分析、疲劳分析等‌

工作内容：
1.进行仿真分析‌：负责对相关产品进行强度疲劳、结构优化、NVH或流体换热噪声等仿真分析‌。
‌2.评估分析结果‌：对分析结果进行评估，形成清晰易懂的报告‌。
‌3.优化设计方案‌：与设计工程师沟通，给出合理的优化设计方案‌。
‌4.仿真与试验对标‌：负责仿真与试验对标，修正分析方法，提升对标精度，总结经验‌。
‌5.跟踪技术发展‌：跟踪和研究国内外CAE仿真分析技术的发展动向‌，编制CAE分析技术条件。
‌6.技术支持：根据软件需求分析，提供设计思路和建议；与测试人员配合完成功能验证及系统优化工作；参与产品规划阶段的技术方案讨论，并提供技术支持意见。
‌7.技术文档和报告‌：负责编写三维模型的几何约束分析、技术报告、技术汇报PPT、软件说明书、软件使用手册、软件理论手册、软件测试手册等‌。
‌8.失效分析和建模‌：负责电驱系统部件等机械系统失效分析建模与分析工作；对产品的设计提出合理的改进建议；协助完成项目的技术文档编写及整理工作；协助解决技术问题和难题；参与产品可靠性评估‌。
9.负责CAE仿真基础技术研究、对标分析、设计技术标准规范及专利的编制‌或制定。

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。',NULL,N'{"sourceFile":"鑫可人员招聘需求表研发250706.xls","sourceWorksheet":"20260706","planCode":"XK-RD-20260706","sequence":6,"jobName":"CAE工程师","plannedHeadcount":1,"salaryRange":"12-30W"}','010499B91C3F3DE4A6F20B0257406DCBD262BD7DADF27FE2AD8319EA01B998B8');
  DECLARE @TaskKey6 VARCHAR(200);
  SET @TaskKey6='knowledge-embed:'+CAST(@Doc6 AS VARCHAR(30))+':'+CAST(@DocVersion6 AS VARCHAR(20));
  IF NOT EXISTS (SELECT 1 FROM dbo.AITask WHERE BusinessKey=@TaskKey6)
    INSERT dbo.AITask(TaskType,KnowledgeDocumentId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey)
    VALUES('KNOWLEDGE_EMBED',@Doc6,'WAITING',5,0,3,@TaskKey6);


  DECLARE @Job7 BIGINT;
  SELECT TOP 1 @Job7=Id FROM dbo.Job WHERE Name=N'CFD仿真工程师' AND IsDeleted=0 ORDER BY Id;
  IF @Job7 IS NULL
  BEGIN
    INSERT dbo.Job(Name,Description,Requirement,SkillRequirement,MinExperienceMonths,EducationRequirement,Status,CreatedBy,IsDeleted)
    VALUES(N'CFD仿真工程师',N'招聘计划批次：20260706
计划人数：1人
薪资范围：12-25W
来源文件：鑫可人员招聘需求表研发250706.xls

工作内容：
1.仿真分析与优化：负责新能源汽车电驱总成（包括电机、控制器、减速器及其集成系统）的流体与热管理CFD仿真分析工作。核心分析内容包括但不限于电机内部油路冷却、喷淋润滑、甩油及两相流分析；齿轮箱搅油润滑、差速器供油、轴承润滑与冷却分析；电机/控制器水冷套流场、压降与换热性能分析；电驱总成整体热管理分析，预测关键部件（如IGBT/SiC模块、定转子、磁钢、轴承、齿轮等）的稳态和瞬态温度场；电驱总成通风、防爆及内部冷凝分析。
2.基于仿真结果，提出结构设计优化方案，并与设计工程师协作，完成设计迭代，提升产品性能与可靠性。
3.主导联合外部资源开发和验证CFD分析模型，提升仿真精度与效率，同时参与电驱总成台架试验和整车测试，进行仿真模型对标与验证，持续修正和优化仿真模型。
4.针对电驱总成开发过程中出现的过热、润滑不足、效率低等热管理与流体问题，进行根因分析，并通过仿真手段提供解决方案。
5.建立和完善电驱总成CFD仿真的标准化流程、分析规范及评价体系。
6.与结构设计、电磁设计、NVH、控制系统等团队紧密合作，进行多物理场（流-固-热-电磁）耦合仿真需求对接与数据传递。
7.编写清晰、专业的仿真分析报告，并向项目团队和管理层汇报分析结果和建议。
‌8.跟踪技术发展‌：跟踪和研究国内外CFD仿真分析技术的发展动向‌。
9.负责CFD仿真基础技术研究、对标分析、设计技术标准规范及专利的编制‌或制定。',N'本科，3年及以上从事CFD仿真分析的工作经验，具备新能源汽车电驱总成或类似旋转机械（如电机、变速箱、泵、风机）的流体与热管理CFD分析经验者优先。1.精通至少一款主流CFD软件，如 Star-CCM+、Fluent、CFX 等。熟练掌握 Star-CCM+ 者优先。
2.熟悉三维建模软件，如 CATIA、UG/NX、SolidWorks 等，具备良好的几何处理和修复能力。
3.扎实的流体力学、传热学、工程热力学 理论基础知识。',N'本科，3年及以上从事CFD仿真分析的工作经验，具备新能源汽车电驱总成或类似旋转机械（如电机、变速箱、泵、风机）的流体与热管理CFD分析经验者优先。1.精通至少一款主流CFD软件，如 Star-CCM+、Fluent、CFX 等。熟练掌握 Star-CCM+ 者优先。
2.熟悉三维建模软件，如 CATIA、UG/NX、SolidWorks 等，具备良好的几何处理和修复能力。
3.扎实的流体力学、传热学、工程热力学 理论基础知识。',36,N'本科','OPEN',@AdminId,0);
    SET @Job7=CAST(SCOPE_IDENTITY() AS BIGINT);
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.ScoreConfig WHERE JobId=@Job7)
    INSERT dbo.ScoreConfig(JobId,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,RuleJson,Version,UpdatedBy)
    VALUES(@Job7,40.00,30.00,20.00,10.00,N'{"schemaVersion":1,"source":{"file":"鑫可人员招聘需求表研发250706.xls","worksheet":"20260706","sha256":"B8DC2EC4FB8FCAA1DBFA0B070513B7E8C02A6EC1D88BADDF765E3B452061D770"},"recruitmentPlan":{"code":"XK-RD-20260706","sequence":7,"plannedHeadcount":1,"salaryRange":"12-25W"},"weights":{"skill":40,"project":30,"experience":20,"education":10}}',1,@AdminId);

  DECLARE @Doc7 BIGINT, @DocVersion7 INT;
  SELECT @Doc7=Id,@DocVersion7=Version FROM dbo.KnowledgeDocument WHERE Code=N'XK-RD-20260706-JOB-07';
  IF @Doc7 IS NULL
  BEGIN
    INSERT dbo.KnowledgeDocument(Code,Title,DocumentType,Content,Status,ContentHash,Version,CreatedBy,IsDeleted)
    VALUES(N'XK-RD-20260706-JOB-07',N'招聘计划：CFD仿真工程师','JOB_REQUIREMENT',N'知识编码：XK-RD-20260706-JOB-07
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：CFD仿真工程师
计划人数：1人
薪资范围：12-25W
最低工作经验：3年（36个月）
学历要求：本科

岗位要求：
本科，3年及以上从事CFD仿真分析的工作经验，具备新能源汽车电驱总成或类似旋转机械（如电机、变速箱、泵、风机）的流体与热管理CFD分析经验者优先。1.精通至少一款主流CFD软件，如 Star-CCM+、Fluent、CFX 等。熟练掌握 Star-CCM+ 者优先。
2.熟悉三维建模软件，如 CATIA、UG/NX、SolidWorks 等，具备良好的几何处理和修复能力。
3.扎实的流体力学、传热学、工程热力学 理论基础知识。

工作内容：
1.仿真分析与优化：负责新能源汽车电驱总成（包括电机、控制器、减速器及其集成系统）的流体与热管理CFD仿真分析工作。核心分析内容包括但不限于电机内部油路冷却、喷淋润滑、甩油及两相流分析；齿轮箱搅油润滑、差速器供油、轴承润滑与冷却分析；电机/控制器水冷套流场、压降与换热性能分析；电驱总成整体热管理分析，预测关键部件（如IGBT/SiC模块、定转子、磁钢、轴承、齿轮等）的稳态和瞬态温度场；电驱总成通风、防爆及内部冷凝分析。
2.基于仿真结果，提出结构设计优化方案，并与设计工程师协作，完成设计迭代，提升产品性能与可靠性。
3.主导联合外部资源开发和验证CFD分析模型，提升仿真精度与效率，同时参与电驱总成台架试验和整车测试，进行仿真模型对标与验证，持续修正和优化仿真模型。
4.针对电驱总成开发过程中出现的过热、润滑不足、效率低等热管理与流体问题，进行根因分析，并通过仿真手段提供解决方案。
5.建立和完善电驱总成CFD仿真的标准化流程、分析规范及评价体系。
6.与结构设计、电磁设计、NVH、控制系统等团队紧密合作，进行多物理场（流-固-热-电磁）耦合仿真需求对接与数据传递。
7.编写清晰、专业的仿真分析报告，并向项目团队和管理层汇报分析结果和建议。
‌8.跟踪技术发展‌：跟踪和研究国内外CFD仿真分析技术的发展动向‌。
9.负责CFD仿真基础技术研究、对标分析、设计技术标准规范及专利的编制‌或制定。

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。','INDEXING','C7203AD037D4DE3B1DDE67D6A2D7ED556D25633018BAC5091A0D2A102C11F36D',1,@AdminId,0);
    SET @Doc7=CAST(SCOPE_IDENTITY() AS BIGINT); SET @DocVersion7=1;
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.KnowledgeChunk WHERE KnowledgeDocumentId=@Doc7 AND ChunkIndex=0)
    INSERT dbo.KnowledgeChunk(KnowledgeDocumentId,ChunkIndex,Content,TokenCount,MetadataJson,ContentHash)
    VALUES(@Doc7,0,N'知识编码：XK-RD-20260706-JOB-07
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：CFD仿真工程师
计划人数：1人
薪资范围：12-25W
最低工作经验：3年（36个月）
学历要求：本科

岗位要求：
本科，3年及以上从事CFD仿真分析的工作经验，具备新能源汽车电驱总成或类似旋转机械（如电机、变速箱、泵、风机）的流体与热管理CFD分析经验者优先。1.精通至少一款主流CFD软件，如 Star-CCM+、Fluent、CFX 等。熟练掌握 Star-CCM+ 者优先。
2.熟悉三维建模软件，如 CATIA、UG/NX、SolidWorks 等，具备良好的几何处理和修复能力。
3.扎实的流体力学、传热学、工程热力学 理论基础知识。

工作内容：
1.仿真分析与优化：负责新能源汽车电驱总成（包括电机、控制器、减速器及其集成系统）的流体与热管理CFD仿真分析工作。核心分析内容包括但不限于电机内部油路冷却、喷淋润滑、甩油及两相流分析；齿轮箱搅油润滑、差速器供油、轴承润滑与冷却分析；电机/控制器水冷套流场、压降与换热性能分析；电驱总成整体热管理分析，预测关键部件（如IGBT/SiC模块、定转子、磁钢、轴承、齿轮等）的稳态和瞬态温度场；电驱总成通风、防爆及内部冷凝分析。
2.基于仿真结果，提出结构设计优化方案，并与设计工程师协作，完成设计迭代，提升产品性能与可靠性。
3.主导联合外部资源开发和验证CFD分析模型，提升仿真精度与效率，同时参与电驱总成台架试验和整车测试，进行仿真模型对标与验证，持续修正和优化仿真模型。
4.针对电驱总成开发过程中出现的过热、润滑不足、效率低等热管理与流体问题，进行根因分析，并通过仿真手段提供解决方案。
5.建立和完善电驱总成CFD仿真的标准化流程、分析规范及评价体系。
6.与结构设计、电磁设计、NVH、控制系统等团队紧密合作，进行多物理场（流-固-热-电磁）耦合仿真需求对接与数据传递。
7.编写清晰、专业的仿真分析报告，并向项目团队和管理层汇报分析结果和建议。
‌8.跟踪技术发展‌：跟踪和研究国内外CFD仿真分析技术的发展动向‌。
9.负责CFD仿真基础技术研究、对标分析、设计技术标准规范及专利的编制‌或制定。

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。',NULL,N'{"sourceFile":"鑫可人员招聘需求表研发250706.xls","sourceWorksheet":"20260706","planCode":"XK-RD-20260706","sequence":7,"jobName":"CFD仿真工程师","plannedHeadcount":1,"salaryRange":"12-25W"}','C7203AD037D4DE3B1DDE67D6A2D7ED556D25633018BAC5091A0D2A102C11F36D');
  DECLARE @TaskKey7 VARCHAR(200);
  SET @TaskKey7='knowledge-embed:'+CAST(@Doc7 AS VARCHAR(30))+':'+CAST(@DocVersion7 AS VARCHAR(20));
  IF NOT EXISTS (SELECT 1 FROM dbo.AITask WHERE BusinessKey=@TaskKey7)
    INSERT dbo.AITask(TaskType,KnowledgeDocumentId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey)
    VALUES('KNOWLEDGE_EMBED',@Doc7,'WAITING',5,0,3,@TaskKey7);


  DECLARE @Job8 BIGINT;
  SELECT TOP 1 @Job8=Id FROM dbo.Job WHERE Name=N'NVH测试工程师' AND IsDeleted=0 ORDER BY Id;
  IF @Job8 IS NULL
  BEGIN
    INSERT dbo.Job(Name,Description,Requirement,SkillRequirement,MinExperienceMonths,EducationRequirement,Status,CreatedBy,IsDeleted)
    VALUES(N'NVH测试工程师',N'招聘计划批次：20260706
计划人数：1人
薪资范围：15-30W
来源文件：鑫可人员招聘需求表研发250706.xls

工作内容：
1.负责动力总成系统、减速器总成NVH测试和故障诊断‌：进行电驱动力总成的噪声和振动特性研究，仿真优化产品NVH性能，协调和指导电机、减速器及控制器系统开发过程中的噪声和振动问题预防、分析和解决‌。
‌2.测试和数据分析‌：负责电驱动系统的NVH测试，处理试验数据，及时反馈异常情况，并编制NVH测试报告，评估测试结果‌。
3.负责公司产品的NVH性能定义、验收、分析优化‌：负责电机、电控、旋转机械类产品的NVH性能定义、验收、分析优化，负责公司NVH领域新技术、新应用的研究及跟踪‌。
4.协助NVH相关软件设计和编程工作‌：协助测试台架NVH相关的软件设计和编程工作，进行测试台架NVH性能数据的分析和处理。
5.参与设计评审会，解决研发过程中出现的问题‌：参与设计评审会，解决研发过程中出现的NVH相关问题，协助部门领导制定NVH管理策略并推动实施‌。
6.NVH不良分析：协助质量部门对量产电驱系统NVH不良分析，制定相应的返修方案。
7.负责NVH仿真基础技术研究、对标分析、设计技术标准规范及专利的编制‌或制定。',N'本科，3年以上经验，熟悉汽车理论、NVH基本理论、常用数据处理方法，熟练操作LMS、Head等常用NVH测试分析软件‌，有强度疲劳、结构优化、NVH或流体换热噪声等仿真分析经开发经验者优先',N'本科，3年以上经验，熟悉汽车理论、NVH基本理论、常用数据处理方法，熟练操作LMS、Head等常用NVH测试分析软件‌，有强度疲劳、结构优化、NVH或流体换热噪声等仿真分析经开发经验者优先',36,N'本科','OPEN',@AdminId,0);
    SET @Job8=CAST(SCOPE_IDENTITY() AS BIGINT);
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.ScoreConfig WHERE JobId=@Job8)
    INSERT dbo.ScoreConfig(JobId,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,RuleJson,Version,UpdatedBy)
    VALUES(@Job8,40.00,30.00,20.00,10.00,N'{"schemaVersion":1,"source":{"file":"鑫可人员招聘需求表研发250706.xls","worksheet":"20260706","sha256":"B8DC2EC4FB8FCAA1DBFA0B070513B7E8C02A6EC1D88BADDF765E3B452061D770"},"recruitmentPlan":{"code":"XK-RD-20260706","sequence":8,"plannedHeadcount":1,"salaryRange":"15-30W"},"weights":{"skill":40,"project":30,"experience":20,"education":10}}',1,@AdminId);

  DECLARE @Doc8 BIGINT, @DocVersion8 INT;
  SELECT @Doc8=Id,@DocVersion8=Version FROM dbo.KnowledgeDocument WHERE Code=N'XK-RD-20260706-JOB-08';
  IF @Doc8 IS NULL
  BEGIN
    INSERT dbo.KnowledgeDocument(Code,Title,DocumentType,Content,Status,ContentHash,Version,CreatedBy,IsDeleted)
    VALUES(N'XK-RD-20260706-JOB-08',N'招聘计划：NVH测试工程师','JOB_REQUIREMENT',N'知识编码：XK-RD-20260706-JOB-08
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：NVH测试工程师
计划人数：1人
薪资范围：15-30W
最低工作经验：3年（36个月）
学历要求：本科

岗位要求：
本科，3年以上经验，熟悉汽车理论、NVH基本理论、常用数据处理方法，熟练操作LMS、Head等常用NVH测试分析软件‌，有强度疲劳、结构优化、NVH或流体换热噪声等仿真分析经开发经验者优先

工作内容：
1.负责动力总成系统、减速器总成NVH测试和故障诊断‌：进行电驱动力总成的噪声和振动特性研究，仿真优化产品NVH性能，协调和指导电机、减速器及控制器系统开发过程中的噪声和振动问题预防、分析和解决‌。
‌2.测试和数据分析‌：负责电驱动系统的NVH测试，处理试验数据，及时反馈异常情况，并编制NVH测试报告，评估测试结果‌。
3.负责公司产品的NVH性能定义、验收、分析优化‌：负责电机、电控、旋转机械类产品的NVH性能定义、验收、分析优化，负责公司NVH领域新技术、新应用的研究及跟踪‌。
4.协助NVH相关软件设计和编程工作‌：协助测试台架NVH相关的软件设计和编程工作，进行测试台架NVH性能数据的分析和处理。
5.参与设计评审会，解决研发过程中出现的问题‌：参与设计评审会，解决研发过程中出现的NVH相关问题，协助部门领导制定NVH管理策略并推动实施‌。
6.NVH不良分析：协助质量部门对量产电驱系统NVH不良分析，制定相应的返修方案。
7.负责NVH仿真基础技术研究、对标分析、设计技术标准规范及专利的编制‌或制定。

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。','INDEXING','5F8A95509302F6762CD0F5DAA5671CD4EC65F2D02C665B332A4E6A5E3E698629',1,@AdminId,0);
    SET @Doc8=CAST(SCOPE_IDENTITY() AS BIGINT); SET @DocVersion8=1;
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.KnowledgeChunk WHERE KnowledgeDocumentId=@Doc8 AND ChunkIndex=0)
    INSERT dbo.KnowledgeChunk(KnowledgeDocumentId,ChunkIndex,Content,TokenCount,MetadataJson,ContentHash)
    VALUES(@Doc8,0,N'知识编码：XK-RD-20260706-JOB-08
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：NVH测试工程师
计划人数：1人
薪资范围：15-30W
最低工作经验：3年（36个月）
学历要求：本科

岗位要求：
本科，3年以上经验，熟悉汽车理论、NVH基本理论、常用数据处理方法，熟练操作LMS、Head等常用NVH测试分析软件‌，有强度疲劳、结构优化、NVH或流体换热噪声等仿真分析经开发经验者优先

工作内容：
1.负责动力总成系统、减速器总成NVH测试和故障诊断‌：进行电驱动力总成的噪声和振动特性研究，仿真优化产品NVH性能，协调和指导电机、减速器及控制器系统开发过程中的噪声和振动问题预防、分析和解决‌。
‌2.测试和数据分析‌：负责电驱动系统的NVH测试，处理试验数据，及时反馈异常情况，并编制NVH测试报告，评估测试结果‌。
3.负责公司产品的NVH性能定义、验收、分析优化‌：负责电机、电控、旋转机械类产品的NVH性能定义、验收、分析优化，负责公司NVH领域新技术、新应用的研究及跟踪‌。
4.协助NVH相关软件设计和编程工作‌：协助测试台架NVH相关的软件设计和编程工作，进行测试台架NVH性能数据的分析和处理。
5.参与设计评审会，解决研发过程中出现的问题‌：参与设计评审会，解决研发过程中出现的NVH相关问题，协助部门领导制定NVH管理策略并推动实施‌。
6.NVH不良分析：协助质量部门对量产电驱系统NVH不良分析，制定相应的返修方案。
7.负责NVH仿真基础技术研究、对标分析、设计技术标准规范及专利的编制‌或制定。

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。',NULL,N'{"sourceFile":"鑫可人员招聘需求表研发250706.xls","sourceWorksheet":"20260706","planCode":"XK-RD-20260706","sequence":8,"jobName":"NVH测试工程师","plannedHeadcount":1,"salaryRange":"15-30W"}','5F8A95509302F6762CD0F5DAA5671CD4EC65F2D02C665B332A4E6A5E3E698629');
  DECLARE @TaskKey8 VARCHAR(200);
  SET @TaskKey8='knowledge-embed:'+CAST(@Doc8 AS VARCHAR(30))+':'+CAST(@DocVersion8 AS VARCHAR(20));
  IF NOT EXISTS (SELECT 1 FROM dbo.AITask WHERE BusinessKey=@TaskKey8)
    INSERT dbo.AITask(TaskType,KnowledgeDocumentId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey)
    VALUES('KNOWLEDGE_EMBED',@Doc8,'WAITING',5,0,3,@TaskKey8);


  DECLARE @Job9 BIGINT;
  SELECT TOP 1 @Job9=Id FROM dbo.Job WHERE Name=N'齿轴设计工程师' AND IsDeleted=0 ORDER BY Id;
  IF @Job9 IS NULL
  BEGIN
    INSERT dbo.Job(Name,Description,Requirement,SkillRequirement,MinExperienceMonths,EducationRequirement,Status,CreatedBy,IsDeleted)
    VALUES(N'齿轴设计工程师',N'招聘计划批次：20260706
计划人数：2人
薪资范围：15-40W
来源文件：鑫可人员招聘需求表研发250706.xls

工作内容：
1.负责变速箱齿轮、轴、行星轮系，轴承等零部件的设计开发及强度校核工作；
2.负责零部件和分总成的0TS认可工作；3.负责轴齿零部件的性能开发及优化提升；4.负责轴齿类零件生产及售后技术支持；5.负责新能源变速箱/新能源电机/自动变速箱等运转部件的设计。条件优秀薪资可谈',N'1、本科及以上学历，机械设计，车辆等相关专业；
2、熟悉齿轮机加工零件的设计和加工工艺；3、具有一定的分析计算能力；4、熟练使用Solidworks、AutoCAD、MAST等设计软件；5、有高速齿轮设计和行星齿轮研发工作经验者优先；6、受过管理学、质量体系、有限元分析等培训；熟悉IATF16949体系。',N'1、本科及以上学历，机械设计，车辆等相关专业；
2、熟悉齿轮机加工零件的设计和加工工艺；3、具有一定的分析计算能力；4、熟练使用Solidworks、AutoCAD、MAST等设计软件；5、有高速齿轮设计和行星齿轮研发工作经验者优先；6、受过管理学、质量体系、有限元分析等培训；熟悉IATF16949体系。',36,N'本科及以上','OPEN',@AdminId,0);
    SET @Job9=CAST(SCOPE_IDENTITY() AS BIGINT);
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.ScoreConfig WHERE JobId=@Job9)
    INSERT dbo.ScoreConfig(JobId,SkillWeight,ProjectWeight,ExperienceWeight,EducationWeight,RuleJson,Version,UpdatedBy)
    VALUES(@Job9,40.00,30.00,20.00,10.00,N'{"schemaVersion":1,"source":{"file":"鑫可人员招聘需求表研发250706.xls","worksheet":"20260706","sha256":"B8DC2EC4FB8FCAA1DBFA0B070513B7E8C02A6EC1D88BADDF765E3B452061D770"},"recruitmentPlan":{"code":"XK-RD-20260706","sequence":9,"plannedHeadcount":2,"salaryRange":"15-40W"},"weights":{"skill":40,"project":30,"experience":20,"education":10}}',1,@AdminId);

  DECLARE @Doc9 BIGINT, @DocVersion9 INT;
  SELECT @Doc9=Id,@DocVersion9=Version FROM dbo.KnowledgeDocument WHERE Code=N'XK-RD-20260706-JOB-09';
  IF @Doc9 IS NULL
  BEGIN
    INSERT dbo.KnowledgeDocument(Code,Title,DocumentType,Content,Status,ContentHash,Version,CreatedBy,IsDeleted)
    VALUES(N'XK-RD-20260706-JOB-09',N'招聘计划：齿轴设计工程师','JOB_REQUIREMENT',N'知识编码：XK-RD-20260706-JOB-09
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：齿轴设计工程师
计划人数：2人
薪资范围：15-40W
最低工作经验：3年（36个月）
学历要求：本科及以上

岗位要求：
1、本科及以上学历，机械设计，车辆等相关专业；
2、熟悉齿轮机加工零件的设计和加工工艺；3、具有一定的分析计算能力；4、熟练使用Solidworks、AutoCAD、MAST等设计软件；5、有高速齿轮设计和行星齿轮研发工作经验者优先；6、受过管理学、质量体系、有限元分析等培训；熟悉IATF16949体系。

工作内容：
1.负责变速箱齿轮、轴、行星轮系，轴承等零部件的设计开发及强度校核工作；
2.负责零部件和分总成的0TS认可工作；3.负责轴齿零部件的性能开发及优化提升；4.负责轴齿类零件生产及售后技术支持；5.负责新能源变速箱/新能源电机/自动变速箱等运转部件的设计。条件优秀薪资可谈

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。','INDEXING','C68165481F1DFAD6C769363B1E7B54F67BFD6B7F835B4827153073BFF5DEDC71',1,@AdminId,0);
    SET @Doc9=CAST(SCOPE_IDENTITY() AS BIGINT); SET @DocVersion9=1;
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.KnowledgeChunk WHERE KnowledgeDocumentId=@Doc9 AND ChunkIndex=0)
    INSERT dbo.KnowledgeChunk(KnowledgeDocumentId,ChunkIndex,Content,TokenCount,MetadataJson,ContentHash)
    VALUES(@Doc9,0,N'知识编码：XK-RD-20260706-JOB-09
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
岗位：齿轴设计工程师
计划人数：2人
薪资范围：15-40W
最低工作经验：3年（36个月）
学历要求：本科及以上

岗位要求：
1、本科及以上学历，机械设计，车辆等相关专业；
2、熟悉齿轮机加工零件的设计和加工工艺；3、具有一定的分析计算能力；4、熟练使用Solidworks、AutoCAD、MAST等设计软件；5、有高速齿轮设计和行星齿轮研发工作经验者优先；6、受过管理学、质量体系、有限元分析等培训；熟悉IATF16949体系。

工作内容：
1.负责变速箱齿轮、轴、行星轮系，轴承等零部件的设计开发及强度校核工作；
2.负责零部件和分总成的0TS认可工作；3.负责轴齿零部件的性能开发及优化提升；4.负责轴齿类零件生产及售后技术支持；5.负责新能源变速箱/新能源电机/自动变速箱等运转部件的设计。条件优秀薪资可谈

简历匹配提示：优先核对候选人的相关行业/产品研发年限、关键设计或仿真工具、独立开发与问题闭环能力，以及岗位要求中明确列出的优先经验。不得仅凭关键词判定，需结合项目经历证据。',NULL,N'{"sourceFile":"鑫可人员招聘需求表研发250706.xls","sourceWorksheet":"20260706","planCode":"XK-RD-20260706","sequence":9,"jobName":"齿轴设计工程师","plannedHeadcount":2,"salaryRange":"15-40W"}','C68165481F1DFAD6C769363B1E7B54F67BFD6B7F835B4827153073BFF5DEDC71');
  DECLARE @TaskKey9 VARCHAR(200);
  SET @TaskKey9='knowledge-embed:'+CAST(@Doc9 AS VARCHAR(30))+':'+CAST(@DocVersion9 AS VARCHAR(20));
  IF NOT EXISTS (SELECT 1 FROM dbo.AITask WHERE BusinessKey=@TaskKey9)
    INSERT dbo.AITask(TaskType,KnowledgeDocumentId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey)
    VALUES('KNOWLEDGE_EMBED',@Doc9,'WAITING',5,0,3,@TaskKey9);


  DECLARE @PlanDoc BIGINT, @PlanVersion INT;
  SELECT @PlanDoc=Id,@PlanVersion=Version FROM dbo.KnowledgeDocument WHERE Code=N'XK-RD-20260706-PLAN';
  IF @PlanDoc IS NULL
  BEGIN
    INSERT dbo.KnowledgeDocument(Code,Title,DocumentType,Content,Status,ContentHash,Version,CreatedBy,IsDeleted)
    VALUES(N'XK-RD-20260706-PLAN',N'鑫可研发招聘计划汇总（20260706）','OTHER',N'知识编码：XK-RD-20260706-PLAN
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
源文件SHA-256：B8DC2EC4FB8FCAA1DBFA0B070513B7E8C02A6EC1D88BADDF765E3B452061D770
岗位数量：9个
计划总人数：15人

岗位清单：
1. 电驱总成集成设计工程师｜2人｜15-50W
2. 电机电磁设计工程师｜1人｜20-45W
3. 电机结构工程师｜3人｜15-35W
4. 传动换挡机构设计工程师｜1人｜10-25W
5. 结构设计工程师｜3人｜10-20W
6. CAE工程师｜1人｜12-30W
7. CFD仿真工程师｜1人｜12-25W
8. NVH测试工程师｜1人｜15-30W
9. 齿轴设计工程师｜2人｜15-40W

评分规则：技能40%，项目经验30%，工作经验20%，学历10%。
使用说明：本知识用于招聘计划汇总与跨岗位检索；具体岗位匹配必须同时参考对应的JOB_REQUIREMENT知识文档。','INDEXING','469BC680025995944ED7E69E64D1A29A3A39B865F365FF0E052B780A913036D7',1,@AdminId,0);
    SET @PlanDoc=CAST(SCOPE_IDENTITY() AS BIGINT); SET @PlanVersion=1;
  END;
  IF NOT EXISTS (SELECT 1 FROM dbo.KnowledgeChunk WHERE KnowledgeDocumentId=@PlanDoc AND ChunkIndex=0)
    INSERT dbo.KnowledgeChunk(KnowledgeDocumentId,ChunkIndex,Content,TokenCount,MetadataJson,ContentHash)
    VALUES(@PlanDoc,0,N'知识编码：XK-RD-20260706-PLAN
招聘计划批次：20260706
来源文件：鑫可人员招聘需求表研发250706.xls
源文件SHA-256：B8DC2EC4FB8FCAA1DBFA0B070513B7E8C02A6EC1D88BADDF765E3B452061D770
岗位数量：9个
计划总人数：15人

岗位清单：
1. 电驱总成集成设计工程师｜2人｜15-50W
2. 电机电磁设计工程师｜1人｜20-45W
3. 电机结构工程师｜3人｜15-35W
4. 传动换挡机构设计工程师｜1人｜10-25W
5. 结构设计工程师｜3人｜10-20W
6. CAE工程师｜1人｜12-30W
7. CFD仿真工程师｜1人｜12-25W
8. NVH测试工程师｜1人｜15-30W
9. 齿轴设计工程师｜2人｜15-40W

评分规则：技能40%，项目经验30%，工作经验20%，学历10%。
使用说明：本知识用于招聘计划汇总与跨岗位检索；具体岗位匹配必须同时参考对应的JOB_REQUIREMENT知识文档。',NULL,N'{"sourceFile":"鑫可人员招聘需求表研发250706.xls","sourceWorksheet":"20260706","sourceSha256":"B8DC2EC4FB8FCAA1DBFA0B070513B7E8C02A6EC1D88BADDF765E3B452061D770","planCode":"XK-RD-20260706","jobCount":9,"plannedHeadcount":15}','469BC680025995944ED7E69E64D1A29A3A39B865F365FF0E052B780A913036D7');
  DECLARE @PlanTaskKey VARCHAR(200);
  SET @PlanTaskKey='knowledge-embed:'+CAST(@PlanDoc AS VARCHAR(30))+':'+CAST(@PlanVersion AS VARCHAR(20));
  IF NOT EXISTS (SELECT 1 FROM dbo.AITask WHERE BusinessKey=@PlanTaskKey)
    INSERT dbo.AITask(TaskType,KnowledgeDocumentId,Status,Priority,RetryCount,MaxRetryCount,BusinessKey)
    VALUES('KNOWLEDGE_EMBED',@PlanDoc,'WAITING',5,0,3,@PlanTaskKey);

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF XACT_STATE()<>0 ROLLBACK TRANSACTION;
  DECLARE @ErrorMessage NVARCHAR(4000), @ErrorSeverity INT, @ErrorState INT;
  SELECT @ErrorMessage=ERROR_MESSAGE(),@ErrorSeverity=ERROR_SEVERITY(),@ErrorState=ERROR_STATE();
  RAISERROR(@ErrorMessage,@ErrorSeverity,@ErrorState);
END CATCH;

SELECT Id,Name,Status,MinExperienceMonths,EducationRequirement FROM dbo.Job WHERE IsDeleted=0 ORDER BY Id;
SELECT j.Name,s.SkillWeight,s.ProjectWeight,s.ExperienceWeight,s.EducationWeight,s.Version FROM dbo.ScoreConfig s INNER JOIN dbo.Job j ON j.Id=s.JobId WHERE j.IsDeleted=0 ORDER BY j.Id;
SELECT Id,Code,Title,DocumentType,Status,Version FROM dbo.KnowledgeDocument WHERE Code LIKE N'XK-RD-20260706-%' ORDER BY Code;
SELECT d.Code,c.ChunkIndex,LEN(c.Content) AS ContentLength FROM dbo.KnowledgeChunk c INNER JOIN dbo.KnowledgeDocument d ON d.Id=c.KnowledgeDocumentId WHERE d.Code LIKE N'XK-RD-20260706-%' ORDER BY d.Code,c.ChunkIndex;
SELECT t.Id,t.TaskType,t.Status,t.BusinessKey FROM dbo.AITask t INNER JOIN dbo.KnowledgeDocument d ON d.Id=t.KnowledgeDocumentId WHERE d.Code LIKE N'XK-RD-20260706-%' ORDER BY t.Id;
