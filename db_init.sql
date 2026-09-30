-- ニュースソース管理テーブル
CREATE TABLE IF NOT EXISTS news_sources (
    id SERIAL PRIMARY KEY,
    domain_name VARCHAR(100),
    rss_feed_url VARCHAR(255),
    url_pattern VARCHAR(255) UNIQUE,
    category VARCHAR(50),
    is_active BOOLEAN DEFAULT TRUE,
    is_allowed_summary BOOLEAN DEFAULT TRUE,
    last_check_timestamp TIMESTAMPTZ,
    notes TEXT,
    createdatetime TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    updatedatetime TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

-- ニュース記事集約テーブル
CREATE TABLE IF NOT EXISTS daily_digest (
    id SERIAL PRIMARY KEY,
    run_timestamp TIMESTAMPTZ NOT NULL,
    genre_name VARCHAR(100),
    article_title VARCHAR(255),
    summary_text TEXT,
    source_url VARCHAR(255),
    source_site VARCHAR(100),
    rank_in_genre INTEGER,
    sendFlag BOOLEAN DEFAULT FALSE,
    createdatetime TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    updatedatetime TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP
);

-- インデックス作成
CREATE INDEX IF NOT EXISTS idx_run_timestamp ON daily_digest(run_timestamp);
CREATE INDEX IF NOT EXISTS idx_genre_name ON daily_digest(genre_name);
