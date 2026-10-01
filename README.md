# NewsScraping (C# / .NET 版)

ローカル自律型ニュース集約・要約システムです。  
WSL (Python) 環境から完全な C# / .NET 10 アプリケーションとして再構築され、Windows 環境下で極めて高速かつ高信頼に動作します。

---

## 🌟 主な特徴

- **高速・低リソース**: C# / .NET 10 によるネイティブな並列実行。Python 版と比べて起動時間やメモリ消費を大幅に削減。
- **高信頼フィードパース**: `CodeHollow.FeedReader` により、RSS 0.9/1.0/2.0、Atom、RDF 各種フィードを正確にパース。
- **Web フォールバック**: RSS 以外のサイトやフィード取得失敗時は `HtmlAgilityPack` による HTML クローリングに自動フォールバック。
- **Ollama AI 要約**: ローカル LLM (`gemma4-uncensored:latest` 等) による 200〜300文字の高精度要約。未起動時やタイムアウト時もクラッシュせずフォールバック。
- **PostgreSQL 連携**: `Npgsql` & `Dapper` を使用した接続プール管理。`newslist` スキーマの自動作成・テーブル初期化・インデックス管理。
- **重複防止メカニズム**:
  1. **3日間の重複投稿禁止（DB登録防止）**: 過去3日間以内に登録済みの同一記事（タイトルまたはURL一致）はDB登録をスキップ。
  2. **1日の登録件数無制限**: 1日に何回実行しても新規記事があれば自動追加され、重複記事は安全に除外。
  3. **多重プロセス防止**: Windows `Mutex` による同一プロセスの同時多重起動の完全ブロック。
- **Serilog ロギング**: コンソール出力に加え、`logs/newsbotyyyyMMdd.log` への日次ローテーション保存。

---

## 📁 プロジェクト構成

```text
newsscrapingCsharp/
├── Models/
│   ├── AppConfig.cs          # YAML/設定ファイルのマッピング
│   ├── DailyDigest.cs        # DB保存モデル
│   ├── NewsSource.cs         # ニュースソースモデル
│   └── ScrapedArticle.cs     # スクレイピング生データモデル
├── Services/
│   ├── DatabaseService.cs    # PostgreSQL初期化、重複判定、保存
│   ├── LlmSummarizerService.cs # Ollama APIによる要約生成
│   ├── NewsAggregatorService.cs # 収集・要約・保存のオーケストレーター
│   ├── RssCollectorService.cs   # RSS/Atom/RDFフィード収集
│   └── WebScraperService.cs    # HTMLスクレイピングフォールバック
├── Utils/
│   ├── HtmlSanitizer.cs      # HTMLタグ除去・文字実体参照デコード
│   └── SingleInstanceMutex.cs # 多重起動防止ミューテックス
├── config.yaml               # DB、LLM、ニュースソース設定
├── db_init.sql               # スキーマ・テーブル初期化DDL
├── Program.cs                # CLIエントリーポイント・DI設定
├── Start_NewsBot.bat         # Windows起動用バッチ
└── run.bat                   # 簡易実行用バッチ
```

---

## 🚀 使い方

### 1. 自己診断テスト (接続確認)
```cmd
dotnet run -- --test
```
PostgreSQL接続、Ollama接続、各ニュースソースのRSS疎通をテストし、サマリーを出力します。

### 2. データベースの初期化
```cmd
dotnet run -- --init-db
```
`newslist` スキーマおよび `news_sources`, `daily_digest` テーブルとインデックスを自動初期化します。

### 3. ステータス・集計件数の確認
```cmd
dotnet run -- --status
```
ジャンルごとの保存記事件数と合計件数を表示します。

### 4. ニュース集約の通常実行
```cmd
dotnet run
```
※ 1日の登録件数・実行回数に制限はありません。過去3日以内に登録済みの同一記事は自動的に重複スキップされ、新着記事のみがDBに登録されます。

### 5. 強制実行
```cmd
dotnet run -- --force
```

### 6. バッチ実行
```cmd
Start_NewsBot.bat
```
Windows タスクスケジューラ登録時にも利用可能です。
