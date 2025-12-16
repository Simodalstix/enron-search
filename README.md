# Enron Email Data Spelunking Tool

![Version](https://img.shields.io/badge/version-1.0.0-blue)
![.NET](https://img.shields.io/badge/-8.0-512BD4?style=flat&logo=dotnet&logoColor=white)
![SQLite](https://img.shields.io/badge/SQLite-003B57?style=flat&logo=sqlite&logoColor=white)
![Platform](https://img.shields.io/badge/platform-linux%20%7C%20windows%20%7C%20macOS-lightgrey)

## Overview

This project implements a small command-line search tool for exploring the Enron email dataset.  
It is designed as an **exploratory search system** to help investigators quickly locate potentially relevant (incriminating) emails using keyword-based queries.

The focus of this implementation is **clear architecture**, **bounded memory usage**, and **explainable trade-offs**, rather than exhaustive ingestion or production-scale completeness.

---

## Quick Start

```bash
# Index emails with a time limit (recommended for demos)
dotnet run index /path/to/emails.csv --time 5

# Search for emails
dotnet run search "fraud and investigation"
dotnet run search "skilling or lay"
dotnet run search "investigat"   # misspelling tolerance
```

---

## Indexing Options

```bash
# Time-limited indexing (1–15 minutes)
dotnet run index emails.csv --time 1     # Quick demo (~33k records)
dotnet run index emails.csv --time 5     # Medium analysis (~168k records)
dotnet run index emails.csv --time 10    # Deeper analysis (~336k records)

# Full indexing (bounded)
dotnet run index emails.csv              # Up to 500k records
```

Indexing is intentionally capped to keep runtime and memory usage reasonable while preserving realistic search behaviour.

---

## Search Features

- Boolean keyword search (`AND`, `OR`)
- Order-independent matching
- Lightweight misspelling tolerance (fallback only)
- Related email discovery by sender
- Fast queries via a precomputed inverted index

---

## Dataset

This tool targets the **Enron Email Dataset (CSV format)** available on Kaggle.

The CSV contains tens of millions of rows due to duplication across recipients and folders.  
Rather than exhaustively indexing all rows, this implementation **indexes a representative subset** to support exploratory analysis within realistic time and memory constraints.

---

## High-Level Design

The system is split into two phases:

### 1. Indexing Phase (one-time cost)

- Streams the CSV row-by-row (no full dataset in memory)
- Normalizes and tokenizes email content
- Builds an inverted index (term → email IDs)
- Persists all data to disk using SQLite
- Uses batched transactions and prepared statements for performance

### 2. Search Phase (fast, repeatable)

- Parses keyword queries
- Applies AND / OR semantics
- Resolves matches using SQL set operations (`INTERSECT`, `UNION`)
- Returns matching email metadata and content

This design trades a slower one-time indexing cost for very fast query execution.

---

## Misspelling Tolerance (Scoped)

Misspelling tolerance is implemented as a **constrained fallback**, not a default behaviour:

- Exact term queries are attempted first
- If no results are found, a limited secondary pass applies:
  - Prefix-based matching (`LIKE 'term%'`)
  - Small edit-distance checks over a bounded candidate set

This improves usability while preserving predictable performance and memory safety.

---

## Memory Constraints

The tool is designed to operate under tight memory limits (<250 MB):

- CSV input is streamed line-by-line
- No full dataset is loaded into memory
- Indexes and hashes are disk-backed (SQLite)
- In-memory structures remain small and bounded

---

## What Was Deliberately Not Implemented

To keep scope controlled and behaviour explainable, the following were intentionally omitted:

- Full dataset ingestion
- Advanced relevance ranking (TF-IDF, BM25)
- Stemming or lemmatization
- Phrase or proximity search
- Date-range filtering
- Multithreaded indexing
- Complex query parsing (NOT, parentheses)

These would be reasonable future enhancements but were deprioritised for clarity and correctness.

---

## Performance Notes (Summary)

Indexing performance was improved through:

- Transaction batching
- Deferred index creation
- Lightweight duplicate detection

These optimizations mirror common production patterns while preserving data integrity.  
Detailed benchmarks and tuning rationale are intentionally out of scope for this README.

---

## Summary

This project demonstrates:

- Memory-safe ingestion of large, unstructured text data
- A classic inverted-index search architecture
- Practical handling of noisy, duplicated real-world datasets
- Measured performance optimization under constrained resources
- Clear, defensible scoping decisions

The goal is not exhaustive legal discovery, but a **simple, explainable exploratory search tool**.
