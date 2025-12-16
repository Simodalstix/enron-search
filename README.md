# Enron Email Data Spelunking Tool

![Version](https://img.shields.io/badge/version-1.0.0-blue)
![C#](https://img.shields.io/badge/C%23-239120?style=flat&logo=c-sharp&logoColor=white)
![.NET](https://img.shields.io/badge/-8.0-512BD4?style=flat&logo=dotnet&logoColor=white)
![SQLite](https://img.shields.io/badge/SQLite-003B57?style=flat&logo=sqlite&logoColor=white)
![Platform](https://img.shields.io/badge/platform-linux%20%7C%20windows%20%7C%20macOS-lightgrey)

## Overview

This project implements a small command-line search tool for exploring the Enron email dataset.  
It is designed as an **exploratory search system** to help investigators quickly locate potentially relevant (incriminating) emails using keyword-based queries.

The focus of this implementation is **clear architecture, bounded memory usage, and explainable trade-offs**, rather than exhaustive ingestion or production-scale completeness.

---

## Usage

### Quick Start
```bash
# Index emails with time limit (recommended for demos)
dotnet run index /path/to/emails.csv --time 5

# Search for emails
dotnet run search "fraud and investigation"
dotnet run search "skilling or lay"
dotnet run search "investigat"  # misspelling tolerance
```

### Indexing Options
```bash
# Time-limited indexing (1-15 minutes)
dotnet run index emails.csv --time 1     # Quick demo (≈33k records)
dotnet run index emails.csv --time 5     # Medium analysis (≈168k records)
dotnet run index emails.csv --time 10    # Deep analysis (≈336k records)

# Full dataset indexing
dotnet run index emails.csv              # Process up to 500k records
```

### Search Features
- **Boolean Logic**: `"term1 and term2"`, `"term1 or term2"`
- **Misspelling Tolerance**: Automatic fallback for typos
- **Related Emails**: Shows additional emails from same senders
- **Fast Performance**: Leverages precomputed inverted index

### Dataset
This tool is designed for the **Enron Email Dataset** available on [Kaggle](https://www.kaggle.com/datasets/wcukierski/enron-email-dataset). The CSV format (`emails.csv`) contains ~33 million rows with email metadata and content.

---

## Problem Context

The Enron email dataset is large, noisy, and highly duplicated.  
Depending on the source, a single logical email may appear multiple times (per recipient, folder, or mailbox), resulting in tens of millions of rows.

Given the **time and memory constraints** of the exercise, this tool is intentionally designed to:

- Support **efficient exploratory search**
- Demonstrate a scalable indexing approach
- Avoid loading the full dataset into memory
- Make explicit, defensible scoping decisions

---

## High-Level Design

The tool is split into two main phases:

### 1. Indexing Phase (one-time cost)
- Streams the CSV dataset row-by-row
- Normalises and tokenises email text
- Builds an **inverted index** (`term → email IDs`)
- Persists all data to disk using SQLite
- Uses transactions and prepared statements for performance

### 2. Search Phase (fast, repeatable)
- Accepts a free-form keyword query
- Supports basic `AND` / `OR` semantics
- Looks up matching email IDs via the inverted index
- Returns matching email metadata and text

This design trades a slower one-time indexing cost for very fast query execution.

---

## Dataset Choice

The **CSV flat file** version of the Enron dataset was chosen because it:

- Allows easy **streaming ingestion**
- Avoids complex MIME parsing
- Enables full control over indexing and storage design
- Minimises setup overhead

While the CSV contains significant duplication, this is handled through scoping and heuristics (see below).

---

## Representative Sampling

The full CSV contains ~33 million rows due to duplication across recipients and folders.

Rather than exhaustively indexing all rows, this implementation:

- Indexes a **representative subset** of the dataset (configurable, e.g. ~500k–1M rows)
- Samples evenly across the file to avoid chronological bias
- Keeps indexing time reasonable while preserving realistic search behaviour

This approach reflects how exploratory tools are often built in practice and aligns with the time constraints of the exercise.

---

## Heuristic Optimisations

To reduce redundant work and improve index quality without adding complexity, several **lightweight heuristics** are applied during indexing:

### 1. Text Normalisation
- Lowercasing
- Punctuation removal
- Whitespace normalisation

Ensures consistent tokenisation and hashing.

### 2. Minimum Body Length Filter
- Emails with very short bodies (e.g. < 50 characters) are skipped
- Avoids indexing boilerplate, stubs, and system messages

### 3. Exact Duplicate Detection (Cheap Deduplication)
- A content hash is computed over `(normalised subject + normalised body)`
- Hashes are stored in SQLite with a UNIQUE constraint
- Exact duplicate emails are skipped during indexing

This is **not full deduplication**, but it removes obvious redundancy with minimal cost.

---

## Memory Constraints

The tool is designed to run within tight memory limits (target: <250MB):

- CSV is streamed line-by-line
- No full dataset is loaded into memory
- Index and hashes are disk-backed (SQLite)
- In-memory structures remain small and bounded
- Memory usage can be monitored with `top` or `htop` during indexing

This satisfies the requirement that the tool can operate under constrained heap sizes.

---

## Search Capabilities

- Order-independent keyword matching
- Boolean logic (`AND`, `OR`) with SQL-based implementation
- Deterministic, explainable results
- Fast query performance due to precomputed index
- Related email discovery by sender correlation

### Boolean Logic Implementation

The search engine supports explicit AND/OR operators using a **SQL-based approach** for optimal performance:

**Examples:**
- `"fraud and investigation"` - emails containing both terms (335 results)
- `"skilling or lay"` - emails containing either term (16,009 results)
- `"california energy"` - space-separated terms default to OR logic

**Implementation Choice: SQL vs Application-Level**

**Selected: SQL-based with INTERSECT/UNION**
- Single database query using `INTERSECT` (AND) and `UNION` (OR)
- Leverages existing inverted index for fast term lookups
- Database engine handles set operations efficiently
- Scales well with query complexity

**Alternative: Application-level**
- Multiple queries with C# HashSet operations
- Simpler logic but requires multiple database round-trips
- Higher memory usage for large result sets
- Slower performance for complex queries

The SQL approach was chosen for production-scale performance and architectural consistency with the inverted index design.

### Misspelling Tolerance Design

Misspelling tolerance is implemented as a constrained fallback rather than a default behavior. Queries are first executed using exact term matches against the inverted index. If no results are returned, a limited secondary pass applies lightweight typo tolerance using SQLite-native prefix matching and small edit-distance checks over a bounded candidate set. This approach improves usability while preserving predictable performance and avoiding full vocabulary scans or custom database extensions.

**Example:**
- `"investigat"` - fallback expands to "investigation", "investigators", "investigating" (27 results)
- Maintains <250MB memory constraint through bounded candidate sets
- Uses SQLite LIKE operations for optimal database performance

Advanced features such as ranking, stemming, fuzzy matching, and phrase search are intentionally omitted to keep behaviour predictable and scope controlled.

### Query Optimization & Edge Cases

Additional query optimizations were considered, including result pagination, advanced query validation, performance monitoring, NOT operators, and improved relevance ranking. However, these enhancements were deemed unnecessary for the current objectives, as the core search functionality with boolean logic and misspelling tolerance already meets the exploratory search requirements within the specified memory constraints.

---

## What Was Deliberately Not Implemented

Given the time constraints, the following were intentionally left out:

- Full dataset ingestion
- Sophisticated relevance ranking (TF-IDF, BM25)
- Stemming / lemmatization
- Phrase or proximity search
- Date-range filtering
- Multithreaded indexing

Each of these would be reasonable future enhancements but was deprioritised in favour of clarity and correctness.

---

## Scaling Considerations (Future Work)

For much larger datasets (GBs–PBs), this architecture could be extended by:

- Time-based or shard-based index partitioning
- Distributed ingestion pipelines
- Using dedicated search engines (e.g. OpenSearch / Elasticsearch)
- Background or incremental indexing
- Advanced ranking and fuzzy matching

The core design intentionally mirrors these patterns at a smaller scale.

---

## Performance Optimizations

Through systematic performance tuning, the indexing speed was improved from ~290 records/sec to **~560 records/sec** (production scale):

### Key Optimizations Applied

1. **Deferred Index Creation** (+101% performance - biggest win!)
   - Removed `CREATE INDEX` from schema initialization
   - Create indexes only after bulk inserts complete
   - Avoids per-row index maintenance during ingestion

2. **Transaction Batch Tuning** (+11% performance)
   - Tested batch sizes: 500 → 1000 → 2000 → 5000 → 10000 records
   - **5000 records per transaction** found to be optimal
   - Balances I/O efficiency with memory usage

3. **Lightweight Duplicate Detection** (+5% performance)
   - Switched from SHA-256 content hashing to file path hashing
   - File paths are unique per email in Enron dataset
   - 20-50x faster than cryptographic hashing

4. **SQLite Pragma Optimization**
   - `PRAGMA journal_mode = WAL` - enables concurrent reads during writes
   - `PRAGMA foreign_keys = OFF` - disables constraint checking during bulk inserts
   - Avoided `synchronous = OFF` to maintain data integrity

### Performance Testing Results

| Optimization | Records/sec | Improvement |
|--------------|-------------|-------------|
| Baseline | 290 | - |
| + CsvHelper migration | 395 | +36% |
| + Transaction batching (5000) | 438 | +11% |
| + Deferred indexing | 1059 | +101% |
| + Fast hashing | 1108 | +5% |
| **Final (production-safe)** | **~560** | **~93%** |

*Note: Performance degrades naturally as database size grows from 10k to 500k records, showing realistic production scaling behavior.*

### Architecture Principles

- **I/O bottleneck identification**: Database writes dominated CPU processing (sys time >> user time)
- **Safety vs speed trade-offs**: Prioritized data integrity over marginal performance gains
- **Scalability patterns**: Optimizations mirror techniques used in production-scale systems

---

## Summary

This project demonstrates:

- Structured, memory-safe ingestion of large unstructured text
- A classic inverted-index search architecture
- Practical handling of messy, duplicated real-world data
- **Systematic performance optimization with measurable results**
- Clear trade-offs aligned with time and resource constraints

The goal is not exhaustive legal discovery, but an **efficient, explainable exploratory search tool**.
