# RecruitAI Python Agent

## 开发启动

```powershell
cd C:\Zhaopin\ai-agent-service
.\run-dev.ps1
```

健康检查：

- `GET http://127.0.0.1:8100/health`
- `GET http://127.0.0.1:8100/health/database`
- `GET http://127.0.0.1:8100/health/deepseek`
- `POST http://127.0.0.1:8100/rag/search`

`.env` 仅用于本机开发并包含开发密钥。复制发布文件时不要将开发密钥带到生产服务器；生产环境使用独立密钥和服务账号配置。

## RESUME_IMPORT Worker

启动前必须先执行数据库 `V002__web_import_review_knowledge.sql`。Worker默认同时处理10个任务（`WORKER_CONCURRENCY=10`），每个执行单元按以下顺序认领任务：

1. 使用 `UPDLOCK + READPAST + ROWLOCK` 领取一个等待中的 `RESUME_IMPORT`；
2. 简历导入仅接受JSON，不接收或处理Word、PDF；
3. JSON只读取文本字段，不启动Office、浏览器、Shell、链接或任何外部程序；
4. 保守识别合并简历边界并生成物理分割文件；分割结果超过100份仍继续当前任务；
5. 单份超过20MB时按图片大小移除，优化前后规范化文本Hash必须一致；
6. 手机和邮箱在本地提取并从AI提示中遮蔽，URL从提示中移除；
7. DeepSeek使用JSON模式生成结构化档案；正式数据库写入使用短事务；
8. 全部成功后删除对应源文件和分割中间文件；业务成功但临时清理失败只写审计，不回滚业务；
9. 任一导入失败直接标记FAILED并把源文件移到异常目录，不自动重试。

JSON简历必须是UTF-8对象，并包含非空字符串 `talent_id`、`source_route`、`candidate_name` 和 `visible_text`，可选 `headline`。文件名必须为 `{来源}_{人才ID}.json`，`ResumeSource` 取第一个下划线前的来源前缀，文件名人才ID必须与内容一致。JSON正文只作为不可信纯文本读取，HR确认后才调用DeepSeek；按既定规则，JSON导入的手机号、邮箱始终保持NULL且不按姓名自动合并候选人。后续同来源、同人才ID的新版简历归入已有候选人。

## KNOWLEDGE_EMBED Worker 与 RAG

Worker在没有待处理简历时领取一个 `KNOWLEDGE_EMBED` 任务：

1. 按段落切分知识正文，默认每块最多1200字符、重叠160字符；
2. 使用离线 `hashing-char-ngram-v1` 生成1024维、L2归一化的中文字符/词组特征向量；
3. 以小端 `float32[]` 写入 SQL Server `VARBINARY(MAX)`，同时记录模型名和维度；
4. 所有新向量生成成功后才在一个事务中替换旧分块和向量；
5. 文档版本变化时取消旧任务，避免旧向量覆盖新正文；
6. Python加载同模型、同维度的ACTIVE知识向量并计算余弦相似度，返回Top-K。

检索示例：

```powershell
$body = @{ query='电机电磁设计需要哪些经验'; top_k=3 } | ConvertTo-Json
Invoke-RestMethod http://127.0.0.1:8100/rag/search -Method Post -ContentType 'application/json' -Body $body
```

开发环境执行一次测试：

```powershell
.\.venv\Scripts\python.exe -m pytest -q
```
