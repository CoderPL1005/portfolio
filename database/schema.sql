-- ============================================================
-- PERSONAL PORTFOLIO + PERSONAL RAG AGENT
-- DATABASE SCHEMA V1
--
-- Stack:
--   PostgreSQL / Neon
--   pgvector
--   ASP.NET Core + EF Core
--   Angular
--   Cloudflare R2
--
-- PRINCIPLE:
--   Portfolio tables are the SOURCE OF TRUTH.
--   knowledge_documents / knowledge_chunks are DERIVED DATA.
-- ============================================================


-- ============================================================
-- EXTENSIONS
-- ============================================================

CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS vector;


-- ============================================================
-- COMMON UPDATED_AT TRIGGER
-- ============================================================

CREATE OR REPLACE FUNCTION set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;



-- ============================================================
-- 1. ADMIN USERS
-- ============================================================

CREATE TABLE admin_users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    email VARCHAR(255) NOT NULL,
    password_hash TEXT NOT NULL,

    full_name VARCHAR(255),

    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    last_login_at TIMESTAMPTZ,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_admin_users_email
        UNIQUE (email)
);


CREATE UNIQUE INDEX uq_admin_users_email_ci
    ON admin_users (LOWER(email));


CREATE TRIGGER trg_admin_users_updated_at
BEFORE UPDATE ON admin_users
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();



-- ============================================================
-- 2. ADMIN REFRESH TOKENS
-- ============================================================

CREATE TABLE admin_refresh_tokens (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    admin_user_id UUID NOT NULL
        REFERENCES admin_users(id)
        ON DELETE CASCADE,

    token_hash TEXT NOT NULL,

    expires_at TIMESTAMPTZ NOT NULL,

    revoked_at TIMESTAMPTZ,

    replaced_by_token_id UUID
        REFERENCES admin_refresh_tokens(id)
        ON DELETE SET NULL,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_admin_refresh_tokens_hash
        UNIQUE (token_hash),

    CONSTRAINT ck_admin_refresh_token_expiry
        CHECK (expires_at > created_at)
);


CREATE INDEX ix_admin_refresh_tokens_user
    ON admin_refresh_tokens(admin_user_id);

CREATE INDEX ix_admin_refresh_tokens_expires
    ON admin_refresh_tokens(expires_at);

CREATE INDEX ix_admin_refresh_tokens_active
    ON admin_refresh_tokens(admin_user_id, expires_at)
    WHERE revoked_at IS NULL;



-- ============================================================
-- 3. MEDIA ASSETS
-- ============================================================

CREATE TABLE media_assets (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    storage_key TEXT NOT NULL,
    public_url TEXT NOT NULL,

    file_name VARCHAR(255) NOT NULL,

    mime_type VARCHAR(150),

    file_size BIGINT,

    alt_text VARCHAR(500),

    media_type VARCHAR(30) NOT NULL DEFAULT 'IMAGE',

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_media_assets_storage_key
        UNIQUE (storage_key),

    CONSTRAINT ck_media_assets_type
        CHECK (
            media_type IN (
                'IMAGE',
                'DOCUMENT',
                'CV',
                'OTHER'
            )
        ),

    CONSTRAINT ck_media_assets_file_size
        CHECK (
            file_size IS NULL
            OR file_size >= 0
        )
);


CREATE TRIGGER trg_media_assets_updated_at
BEFORE UPDATE ON media_assets
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();



-- ============================================================
-- 4. PROFILE
--
-- Portfolio V1 has exactly one canonical profile.
-- singleton_key prevents accidentally creating multiple profiles.
-- ============================================================

CREATE TABLE profiles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    singleton_key SMALLINT NOT NULL DEFAULT 1,

    full_name VARCHAR(255) NOT NULL,

    professional_title VARCHAR(255),

    secondary_title VARCHAR(255),

    hero_headline VARCHAR(500),

    hero_summary TEXT,

    about_markdown TEXT,

    email VARCHAR(255),

    phone VARCHAR(50),

    location VARCHAR(255),

    university VARCHAR(255),

    major VARCHAR(255),

    availability_status VARCHAR(150),

    profile_image_id UUID
        REFERENCES media_assets(id)
        ON DELETE SET NULL,

    cv_media_id UUID
        REFERENCES media_assets(id)
        ON DELETE SET NULL,

    is_published BOOLEAN NOT NULL DEFAULT TRUE,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_profiles_singleton
        UNIQUE (singleton_key),

    CONSTRAINT ck_profiles_singleton
        CHECK (singleton_key = 1)
);


CREATE TRIGGER trg_profiles_updated_at
BEFORE UPDATE ON profiles
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();



-- ============================================================
-- 5. EXPERIENCES
-- ============================================================

CREATE TABLE experiences (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    company_name VARCHAR(255) NOT NULL,

    role_title VARCHAR(255) NOT NULL,

    location VARCHAR(255),

    start_date DATE NOT NULL,

    end_date DATE,

    is_current BOOLEAN NOT NULL DEFAULT FALSE,

    summary TEXT,

    responsibilities_markdown TEXT,

    company_url TEXT,

    display_order INTEGER NOT NULL DEFAULT 0,

    is_published BOOLEAN NOT NULL DEFAULT TRUE,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_experiences_dates
        CHECK (
            end_date IS NULL
            OR end_date >= start_date
        ),

    CONSTRAINT ck_experiences_current
        CHECK (
            NOT is_current
            OR end_date IS NULL
        )
);


CREATE INDEX ix_experiences_public_order
    ON experiences(is_published, display_order);


CREATE TRIGGER trg_experiences_updated_at
BEFORE UPDATE ON experiences
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();



-- ============================================================
-- 6. TECHNOLOGIES
-- ============================================================

CREATE TABLE technologies (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    name VARCHAR(100) NOT NULL,

    category VARCHAR(50) NOT NULL,

    icon_key VARCHAR(100),

    website_url TEXT,

    display_order INTEGER NOT NULL DEFAULT 0,

    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);


CREATE UNIQUE INDEX uq_technologies_name_ci
    ON technologies (LOWER(name));


CREATE INDEX ix_technologies_category
    ON technologies(category, display_order);


CREATE TRIGGER trg_technologies_updated_at
BEFORE UPDATE ON technologies
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();



-- ============================================================
-- 7. EXPERIENCE TECHNOLOGIES
-- ============================================================

CREATE TABLE experience_technologies (
    experience_id UUID NOT NULL
        REFERENCES experiences(id)
        ON DELETE CASCADE,

    technology_id UUID NOT NULL
        REFERENCES technologies(id)
        ON DELETE CASCADE,

    display_order INTEGER NOT NULL DEFAULT 0,

    PRIMARY KEY (
        experience_id,
        technology_id
    )
);



-- ============================================================
-- 8. PROJECTS
-- ============================================================

CREATE TABLE projects (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    slug VARCHAR(180) NOT NULL,

    title VARCHAR(255) NOT NULL,

    subtitle VARCHAR(500),

    short_description TEXT,

    overview_markdown TEXT,

    role VARCHAR(255),

    team_size INTEGER,

    start_date DATE,

    end_date DATE,

    status VARCHAR(50) NOT NULL DEFAULT 'COMPLETED',

    github_url TEXT,

    live_url TEXT,

    thumbnail_media_id UUID
        REFERENCES media_assets(id)
        ON DELETE SET NULL,

    featured BOOLEAN NOT NULL DEFAULT FALSE,

    is_published BOOLEAN NOT NULL DEFAULT TRUE,

    display_order INTEGER NOT NULL DEFAULT 0,

    seo_title VARCHAR(255),

    seo_description VARCHAR(500),

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_projects_team_size
        CHECK (
            team_size IS NULL
            OR team_size > 0
        ),

    CONSTRAINT ck_projects_status
        CHECK (
            status IN (
                'PLANNED',
                'IN_PROGRESS',
                'ACTIVE',
                'COMPLETED',
                'ARCHIVED'
            )
        ),

    CONSTRAINT ck_projects_dates
        CHECK (
            end_date IS NULL
            OR start_date IS NULL
            OR end_date >= start_date
        )
);


CREATE UNIQUE INDEX uq_projects_slug_ci
    ON projects(LOWER(slug));


CREATE INDEX ix_projects_public
    ON projects(
        is_published,
        featured,
        display_order
    );


CREATE TRIGGER trg_projects_updated_at
BEFORE UPDATE ON projects
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();



-- ============================================================
-- 9. PROJECT TECHNOLOGIES
-- ============================================================

CREATE TABLE project_technologies (
    project_id UUID NOT NULL
        REFERENCES projects(id)
        ON DELETE CASCADE,

    technology_id UUID NOT NULL
        REFERENCES technologies(id)
        ON DELETE CASCADE,

    display_order INTEGER NOT NULL DEFAULT 0,

    PRIMARY KEY (
        project_id,
        technology_id
    )
);


CREATE INDEX ix_project_technologies_order
    ON project_technologies(
        project_id,
        display_order
    );



-- ============================================================
-- 10. PROJECT SECTIONS
-- ============================================================

CREATE TABLE project_sections (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    project_id UUID NOT NULL
        REFERENCES projects(id)
        ON DELETE CASCADE,

    section_type VARCHAR(50) NOT NULL,

    title VARCHAR(255),

    subtitle VARCHAR(500),

    content_markdown TEXT,

    content_json JSONB NOT NULL DEFAULT '{}'::jsonb,

    display_order INTEGER NOT NULL DEFAULT 0,

    is_visible BOOLEAN NOT NULL DEFAULT TRUE,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_project_sections_type
        CHECK (
            section_type IN (
                'OVERVIEW',
                'RESPONSIBILITIES',
                'ARCHITECTURE',
                'ENGINEERING_FOCUS',
                'FEATURES',
                'CHALLENGES',
                'LEARNINGS',
                'SCREENSHOTS',
                'CUSTOM'
            )
        )
);


CREATE INDEX ix_project_sections_project_order
    ON project_sections(
        project_id,
        display_order
    );


CREATE TRIGGER trg_project_sections_updated_at
BEFORE UPDATE ON project_sections
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();



-- ============================================================
-- 11. PROJECT MEDIA
-- ============================================================

CREATE TABLE project_media (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    project_id UUID NOT NULL
        REFERENCES projects(id)
        ON DELETE CASCADE,

    media_asset_id UUID NOT NULL
        REFERENCES media_assets(id)
        ON DELETE CASCADE,

    media_role VARCHAR(30) NOT NULL DEFAULT 'SCREENSHOT',

    caption VARCHAR(500),

    display_order INTEGER NOT NULL DEFAULT 0,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_project_media
        UNIQUE (
            project_id,
            media_asset_id,
            media_role
        ),

    CONSTRAINT ck_project_media_role
        CHECK (
            media_role IN (
                'THUMBNAIL',
                'SCREENSHOT',
                'ARCHITECTURE',
                'DIAGRAM',
                'OTHER'
            )
        )
);


CREATE INDEX ix_project_media_order
    ON project_media(
        project_id,
        media_role,
        display_order
    );



-- ============================================================
-- 12. SKILLS
-- ============================================================

CREATE TABLE skills (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    name VARCHAR(100) NOT NULL,

    category VARCHAR(50) NOT NULL,

    experience_level VARCHAR(30) NOT NULL DEFAULT 'USED',

    description TEXT,

    technology_id UUID
        REFERENCES technologies(id)
        ON DELETE SET NULL,

    display_order INTEGER NOT NULL DEFAULT 0,

    is_published BOOLEAN NOT NULL DEFAULT TRUE,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_skills_experience_level
        CHECK (
            experience_level IN (
                'USED',
                'LEARNING',
                'EXPLORING'
            )
        )
);


CREATE UNIQUE INDEX uq_skills_name_category_ci
    ON skills(
        LOWER(name),
        LOWER(category)
    );


CREATE INDEX ix_skills_public_category
    ON skills(
        is_published,
        category,
        display_order
    );


CREATE TRIGGER trg_skills_updated_at
BEFORE UPDATE ON skills
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();



-- ============================================================
-- 13. EDUCATIONS
-- ============================================================

CREATE TABLE educations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    institution VARCHAR(255) NOT NULL,

    degree VARCHAR(255),

    field_of_study VARCHAR(255),

    start_date DATE,

    end_date DATE,

    description TEXT,

    location VARCHAR(255),

    display_order INTEGER NOT NULL DEFAULT 0,

    is_published BOOLEAN NOT NULL DEFAULT TRUE,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_educations_dates
        CHECK (
            end_date IS NULL
            OR start_date IS NULL
            OR end_date >= start_date
        )
);


CREATE INDEX ix_educations_public_order
    ON educations(
        is_published,
        display_order
    );


CREATE TRIGGER trg_educations_updated_at
BEFORE UPDATE ON educations
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();



-- ============================================================
-- 14. TRAININGS
-- ============================================================

CREATE TABLE trainings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    title VARCHAR(255) NOT NULL,

    provider VARCHAR(255),

    description TEXT,

    start_date DATE,

    end_date DATE,

    credential_url TEXT,

    display_order INTEGER NOT NULL DEFAULT 0,

    is_published BOOLEAN NOT NULL DEFAULT TRUE,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_trainings_dates
        CHECK (
            end_date IS NULL
            OR start_date IS NULL
            OR end_date >= start_date
        )
);


CREATE INDEX ix_trainings_public_order
    ON trainings(
        is_published,
        display_order
    );


CREATE TRIGGER trg_trainings_updated_at
BEFORE UPDATE ON trainings
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();



-- ============================================================
-- 15. CERTIFICATES
-- ============================================================

CREATE TABLE certificates (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    name VARCHAR(255) NOT NULL,

    issuer VARCHAR(255),

    issued_at DATE,

    expires_at DATE,

    credential_id VARCHAR(255),

    credential_url TEXT,

    certificate_media_id UUID
        REFERENCES media_assets(id)
        ON DELETE SET NULL,

    display_order INTEGER NOT NULL DEFAULT 0,

    is_published BOOLEAN NOT NULL DEFAULT TRUE,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_certificates_dates
        CHECK (
            expires_at IS NULL
            OR issued_at IS NULL
            OR expires_at >= issued_at
        )
);


CREATE INDEX ix_certificates_public_order
    ON certificates(
        is_published,
        display_order
    );


CREATE TRIGGER trg_certificates_updated_at
BEFORE UPDATE ON certificates
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();



-- ============================================================
-- 16. ENGINEERING JOURNEY
-- ============================================================

CREATE TABLE journey_items (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    title VARCHAR(255) NOT NULL,

    subtitle VARCHAR(255),

    description TEXT,

    occurred_at DATE,

    icon_key VARCHAR(100),

    display_order INTEGER NOT NULL DEFAULT 0,

    is_published BOOLEAN NOT NULL DEFAULT TRUE,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);


CREATE INDEX ix_journey_items_public_order
    ON journey_items(
        is_published,
        display_order
    );


CREATE TRIGGER trg_journey_items_updated_at
BEFORE UPDATE ON journey_items
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();



-- ============================================================
-- 17. SOCIAL LINKS
-- ============================================================

CREATE TABLE social_links (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    platform VARCHAR(100) NOT NULL,

    label VARCHAR(100),

    url TEXT NOT NULL,

    icon_key VARCHAR(100),

    display_order INTEGER NOT NULL DEFAULT 0,

    is_visible BOOLEAN NOT NULL DEFAULT TRUE,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);


CREATE INDEX ix_social_links_visible_order
    ON social_links(
        is_visible,
        display_order
    );


CREATE TRIGGER trg_social_links_updated_at
BEFORE UPDATE ON social_links
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();



-- ============================================================
-- 18. SITE SETTINGS
-- ============================================================

CREATE TABLE site_settings (
    key VARCHAR(150) PRIMARY KEY,

    value JSONB NOT NULL,

    description VARCHAR(500),

    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);


CREATE TRIGGER trg_site_settings_updated_at
BEFORE UPDATE ON site_settings
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();



-- ============================================================
-- 20. AGENT SETTINGS
-- ============================================================

CREATE TABLE agent_settings (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    name VARCHAR(100) NOT NULL DEFAULT 'portfolio-agent',

    enabled BOOLEAN NOT NULL DEFAULT TRUE,

    provider VARCHAR(100),

    model_name VARCHAR(150),

    embedding_provider VARCHAR(100),

    embedding_model VARCHAR(150),

    embedding_dimensions INTEGER NOT NULL DEFAULT 1536,

    system_prompt TEXT NOT NULL,

    welcome_message TEXT,

    fallback_message TEXT,

    max_context_chunks INTEGER NOT NULL DEFAULT 6,

    minimum_similarity NUMERIC(6,5),

    temperature NUMERIC(4,3) NOT NULL DEFAULT 0.2,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_agent_settings_name
        UNIQUE (name),

    CONSTRAINT ck_agent_context_chunks
        CHECK (
            max_context_chunks BETWEEN 1 AND 20
        ),

    CONSTRAINT ck_agent_similarity
        CHECK (
            minimum_similarity IS NULL
            OR minimum_similarity BETWEEN 0 AND 1
        ),

    CONSTRAINT ck_agent_temperature
        CHECK (
            temperature BETWEEN 0 AND 2
        ),

    CONSTRAINT ck_agent_embedding_dimensions
        CHECK (
            embedding_dimensions = 1536
        )
);


CREATE TRIGGER trg_agent_settings_updated_at
BEFORE UPDATE ON agent_settings
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();



-- ============================================================
-- 21. KNOWLEDGE DOCUMENTS
-- ============================================================

CREATE TABLE knowledge_documents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    source_type VARCHAR(50) NOT NULL,

    source_ref_id UUID,

    source_key VARCHAR(255) NOT NULL,

    title VARCHAR(500) NOT NULL,

    content TEXT NOT NULL,

    content_hash VARCHAR(128) NOT NULL,

    source_updated_at TIMESTAMPTZ,

    version INTEGER NOT NULL DEFAULT 1,

    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,

    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    indexing_status VARCHAR(30) NOT NULL DEFAULT 'PENDING',

    indexed_at TIMESTAMPTZ,

    last_index_error TEXT,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_knowledge_documents_source_key
        UNIQUE (source_key),

    CONSTRAINT ck_knowledge_documents_version
        CHECK (version >= 1),

    CONSTRAINT ck_knowledge_source_type
        CHECK (
            source_type IN (
                'PROFILE',
                'EXPERIENCE',
                'PROJECT',
                'SKILLS',
                'EDUCATION',
                'TRAINING',
                'CERTIFICATE',
                'JOURNEY',
                'MANUAL'
            )
        ),

    CONSTRAINT ck_knowledge_indexing_status
        CHECK (
            indexing_status IN (
                'PENDING',
                'INDEXING',
                'INDEXED',
                'FAILED'
            )
        )
);


CREATE INDEX ix_knowledge_documents_source
    ON knowledge_documents(
        source_type,
        source_ref_id
    );


CREATE INDEX ix_knowledge_documents_index_queue
    ON knowledge_documents(
        indexing_status,
        updated_at
    )
    WHERE
        is_active = TRUE
        AND indexing_status IN ('PENDING', 'FAILED');


CREATE TRIGGER trg_knowledge_documents_updated_at
BEFORE UPDATE ON knowledge_documents
FOR EACH ROW
EXECUTE FUNCTION set_updated_at();



-- ============================================================
-- 22. KNOWLEDGE CHUNKS
--
-- V1 embedding contract:
-- VECTOR(1536)
-- ============================================================

CREATE TABLE knowledge_chunks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    knowledge_document_id UUID NOT NULL
        REFERENCES knowledge_documents(id)
        ON DELETE CASCADE,

    chunk_index INTEGER NOT NULL,

    content TEXT NOT NULL,

    token_count INTEGER,

    content_hash VARCHAR(128),

    embedding VECTOR(1536) NOT NULL,

    embedding_model VARCHAR(150) NOT NULL,

    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_knowledge_chunks_index
        UNIQUE (
            knowledge_document_id,
            chunk_index
        ),

    CONSTRAINT ck_knowledge_chunks_index
        CHECK (
            chunk_index >= 0
        ),

    CONSTRAINT ck_knowledge_chunks_tokens
        CHECK (
            token_count IS NULL
            OR token_count >= 0
        )
);


CREATE INDEX ix_knowledge_chunks_document
    ON knowledge_chunks(
        knowledge_document_id,
        chunk_index
    );


CREATE INDEX ix_knowledge_chunks_embedding_hnsw
    ON knowledge_chunks
    USING hnsw (
        embedding vector_cosine_ops
    );



-- ============================================================
-- 23. CHAT SESSIONS
-- ============================================================

CREATE TABLE chat_sessions (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    public_session_id UUID NOT NULL DEFAULT gen_random_uuid(),

    status VARCHAR(20) NOT NULL DEFAULT 'ACTIVE',

    started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    last_message_at TIMESTAMPTZ,

    closed_at TIMESTAMPTZ,

    message_count INTEGER NOT NULL DEFAULT 0,

    user_message_count INTEGER NOT NULL DEFAULT 0,

    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,

    CONSTRAINT uq_chat_sessions_public_session_id
        UNIQUE (public_session_id),

    CONSTRAINT ck_chat_sessions_status
        CHECK (
            status IN (
                'ACTIVE',
                'CLOSED'
            )
        ),

    CONSTRAINT ck_chat_sessions_message_count
        CHECK (
            message_count >= 0
        ),

    CONSTRAINT ck_chat_sessions_user_message_count
        CHECK (
            user_message_count >= 0
        )
);


CREATE INDEX ix_chat_sessions_status_last_message
    ON chat_sessions(
        status,
        last_message_at DESC
    );



-- ============================================================
-- 24. CHAT MESSAGES
-- ============================================================

CREATE TABLE chat_messages (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    chat_session_id UUID NOT NULL
        REFERENCES chat_sessions(id)
        ON DELETE CASCADE,

    role VARCHAR(20) NOT NULL,

    content TEXT NOT NULL,

    model_name VARCHAR(150),

    prompt_tokens INTEGER,

    completion_tokens INTEGER,

    latency_ms INTEGER,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT ck_chat_messages_role
        CHECK (
            role IN (
                'USER',
                'ASSISTANT',
                'SYSTEM'
            )
        ),

    CONSTRAINT ck_chat_messages_prompt_tokens
        CHECK (
            prompt_tokens IS NULL
            OR prompt_tokens >= 0
        ),

    CONSTRAINT ck_chat_messages_completion_tokens
        CHECK (
            completion_tokens IS NULL
            OR completion_tokens >= 0
        ),

    CONSTRAINT ck_chat_messages_latency
        CHECK (
            latency_ms IS NULL
            OR latency_ms >= 0
        )
);


CREATE INDEX ix_chat_messages_session_created
    ON chat_messages(
        chat_session_id,
        created_at
    );



-- ============================================================
-- 25. CHAT MESSAGE SOURCES
-- ============================================================

CREATE TABLE chat_message_sources (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    chat_message_id UUID NOT NULL
        REFERENCES chat_messages(id)
        ON DELETE CASCADE,

    knowledge_chunk_id UUID NOT NULL
        REFERENCES knowledge_chunks(id)
        ON DELETE CASCADE,

    rank INTEGER NOT NULL,

    similarity_score NUMERIC(8,7),

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_chat_message_sources
        UNIQUE (
            chat_message_id,
            knowledge_chunk_id
        ),

    CONSTRAINT ck_chat_message_sources_rank
        CHECK (
            rank >= 1
        ),

    CONSTRAINT ck_chat_message_sources_similarity
        CHECK (
            similarity_score IS NULL
            OR similarity_score BETWEEN 0 AND 1
        )
);


CREATE INDEX ix_chat_message_sources_message_rank
    ON chat_message_sources(
        chat_message_id,
        rank
    );



-- ============================================================
-- 26. CHAT MESSAGE FEEDBACK
-- ============================================================

CREATE TABLE chat_message_feedback (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    chat_message_id UUID NOT NULL
        REFERENCES chat_messages(id)
        ON DELETE CASCADE,

    rating VARCHAR(20) NOT NULL,

    comment TEXT,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT uq_chat_message_feedback_message
        UNIQUE (chat_message_id),

    CONSTRAINT ck_chat_message_feedback_rating
        CHECK (
            rating IN (
                'POSITIVE',
                'NEGATIVE'
            )
        )
);



-- ============================================================
-- 27. CHAT USAGE DAILY
-- ============================================================

CREATE TABLE chat_usage_daily (
    usage_date DATE NOT NULL,

    visitor_key VARCHAR(80) NOT NULL,

    accepted_message_count INTEGER NOT NULL DEFAULT 0,

    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT chat_usage_daily_pkey
        PRIMARY KEY (usage_date, visitor_key),

    CONSTRAINT ck_chat_usage_daily_count
        CHECK (
            accepted_message_count >= 0
        )
);
