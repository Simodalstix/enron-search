# Enron Email Data Spelunking Tool

## Overview

This project implements a small command-line search tool for exploring the Enron email dataset.  
It is designed as an **exploratory search system** to help investigators quickly locate potentially relevant (incriminating) emails using keyword-based queries.

The focus of this implementation is **clear architecture, bounded memory usage, and explainable trade-offs**, rather than exhaustive ingestion or production-scale completeness.

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

The tool is designed to run within tight memory limits:

- CSV is streamed line-by-line
- No full dataset is loaded into memory
- Index and hashes are disk-backed (SQLite)
- In-memory structures remain small and bounded

This satisfies the requirement that the tool can operate under constrained heap sizes.

---

## Search Capabilities

- Order-independent keyword matching
- Basic boolean logic (`AND`, `OR`)
- Deterministic, explainable results
- Fast query performance due to precomputed index

Advanced features such as ranking, stemming, fuzzy matching, and phrase search are intentionally omitted to keep behaviour predictable and scope controlled.

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

## Summary

This project demonstrates:

- Structured, memory-safe ingestion of large unstructured text
- A classic inverted-index search architecture
- Practical handling of messy, duplicated real-world data
- Clear trade-offs aligned with time and resource constraints

The goal is not exhaustive legal discovery, but an **efficient, explainable exploratory search tool**.
