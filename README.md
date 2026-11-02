# NUMOSQL

NUMOSQL is a small embedded SQL database written in C# for .NET 10. It runs in your process, stores each database
in a folder, and speaks a subset of standard SQL. It adds two extension statements, `CHECKPOINT` and `SHOW TABLES`.

NUMOSQL is a prototype. It is not a production replacement for PostgreSQL. It has one writer at a time, no
multi-column keys, no secondary indexes, and no query planner beyond primary-key lookups.

## Requirements

- .NET 10 SDK

## Build and test

```bash
dotnet test NumoSql.slnx
```

## Command-line shell

Start the shell with a folder for the database. The folder is created if it does not exist:

```bash
dotnet run --project src/NumoSql.Cli -- ./my-database
```

End each statement with `;`. A statement can span several lines. Type `\q` on a line by itself to quit.

```text
numosql> CREATE TABLE people (id INTEGER PRIMARY KEY, name TEXT NOT NULL, age INTEGER);
CREATE TABLE
numosql> INSERT INTO people VALUES (1, 'Ana', 31), (2, 'Bo', NULL);
INSERT 0 2
numosql> SELECT name, age FROM people ORDER BY id;
name | age
-----+-----
Ana  | 31
Bo   | NULL
SELECT 2
numosql> \q
```

## Using NUMOSQL from C#

Reference the engine project from your application:

```xml
<ProjectReference Include="path/to/NumoSql/src/NumoSql/NumoSql.csproj" />
```

Then open a database, create a session, and run SQL:

```csharp
using NumoSql;

using var database = Database.Open("./my-database");
using var session = database.CreateSession();

session.Execute("CREATE TABLE IF NOT EXISTS people (id INTEGER PRIMARY KEY, name TEXT NOT NULL, age INTEGER);");
session.Execute("INSERT INTO people VALUES (1, 'Ana', 31), (2, 'Bo', NULL);");

// Execute returns one QueryResult per statement in the text.
var result = session.Execute("SELECT id, name FROM people WHERE age IS NOT NULL")[0];
foreach (var row in result.Rows)
{
    Console.WriteLine($"{row[0]} {row[1]}");
}
```

Dispose sessions before the database that created them.

### Results

`Execute` returns a `QueryResult` for each statement:

| Property      | Meaning                                                              |
|---------------|----------------------------------------------------------------------|
| `ColumnNames` | Output column names, for `SELECT` and `SHOW TABLES`.                  |
| `Rows`        | Output rows. Each row is an `object?[]`.                              |
| `RowsAffected`| Rows touched by `INSERT`, `UPDATE` or `DELETE`.                       |
| `CommandTag`  | The command, in PostgreSQL style, such as `SELECT 2` or `INSERT 0 2`. |

A cell is a `long` for `INTEGER`, a `double` for `REAL`, a `string` for `TEXT`, a `bool` for `BOOLEAN`, or `null`
for SQL `NULL`.

### Errors

| Exception                 | When it is thrown                                                         |
|---------------------------|---------------------------------------------------------------------------|
| `SqlSyntaxException`      | The SQL text does not parse. `Position` gives the character offset.       |
| `NumoSqlException`        | Any other problem you can act on: unknown table or column, constraint violation, type error, division by zero, or the database already open in another process. `SqlSyntaxException` derives from it. |
| `DatabaseCorruptException`| The files on disk fail a checksum or cannot be recovered. The database will not open. |

A syntax error anywhere in the text runs nothing. A runtime error stops the text at that statement. Statements
before it have already committed if they ran outside a transaction.

## Transactions

Outside a transaction each statement commits by itself.

```sql
BEGIN;
UPDATE accounts SET balance = balance - 50 WHERE id = 1;
UPDATE accounts SET balance = balance + 50 WHERE id = 2;
COMMIT;
```

- `BEGIN` holds the database's write gate until `COMMIT` or `ROLLBACK`. Other sessions wait.
- An error inside a transaction undoes its changes at once and aborts the transaction. Later statements are refused
  until you send `ROLLBACK`. A `COMMIT` on an aborted transaction reports the tag `ROLLBACK`.
- `CHECKPOINT` cannot run inside a transaction.
- Disposing a session with an open transaction rolls it back.

## Persistence and recovery

A database is a folder with two files:

- `numosql.wal` - the write-ahead log. Each commit is written and flushed to disk before it is visible.
- `numosql.checkpoint` - a snapshot written by `CHECKPOINT`.

On open, NUMOSQL loads the latest checkpoint and replays the log after it. A partly written final record is
discarded, because it was never reported as committed. Damage to records that are followed by intact data is
reported as `DatabaseCorruptException`, and the database does not open.

The log grows until you run `CHECKPOINT`. There is no automatic checkpoint.

Only one process may open a database at a time. A second `Database.Open` on the same folder fails with
`NumoSqlException`. Do not delete or edit the files while a database is open.

## SQL reference

### Data types

| Type      | Also accepted as | Stored as |
|-----------|------------------|-----------|
| `INTEGER` | `INT`            | 64-bit signed integer |
| `REAL`    |                  | 64-bit floating point |
| `TEXT`    |                  | string, compared by UTF-16 code unit |
| `BOOLEAN` |                  | `TRUE` or `FALSE` |

An `INTEGER` value cannot go into a `TEXT` column, and the reverse is also an error. An integer goes into a `REAL`
column and is stored as a real.

### Names and literals

- Unquoted names are folded to lower case, so `People` and `people` are the same table.
- Double quotes make a name case-sensitive and allow reserved words: `CREATE TABLE "select" ("from" INTEGER);`.
- Strings use single quotes. Double a quote to include one: `'it''s'`.
- Numbers: `42`, `-7`, `2.5`, `.5`, `5.`, `1e3`.
- Keywords such as `SELECT`, `FROM`, `WHERE`, `ORDER`, `LIMIT`, `NULL`, `TRUE` and `FALSE` are reserved. `KEY`,
  `TABLES` and `TRANSACTION` are not, so `key` and `value` work as column names.
- Comments: `-- to end of line` and `/* block */`.

### Statements

```sql
CREATE TABLE [IF NOT EXISTS] name (column type [NOT NULL] [PRIMARY KEY], ...);
CREATE TABLE name (column type, ..., PRIMARY KEY (column));
DROP TABLE [IF EXISTS] name;

INSERT INTO name [(column, ...)] VALUES (expr, ...) [, (expr, ...)] ...;
UPDATE name SET column = expr [, column = expr] ... [WHERE condition];
DELETE FROM name [WHERE condition];

SELECT item [, item] ...
  [FROM table [[AS] alias]
     [INNER JOIN table [[AS] alias] ON condition] ...]
  [WHERE condition]
  [GROUP BY column [, column] ...]
  [ORDER BY expr [ASC | DESC] [, ...]]
  [LIMIT n] [OFFSET n];

BEGIN [TRANSACTION];
COMMIT [TRANSACTION];
ROLLBACK [TRANSACTION];

-- NUMOSQL extensions
CHECKPOINT;
SHOW TABLES;
```

Notes:

- A primary key is one column. It must be unique and cannot be NULL. Both the column-level and table-level forms work.
- `SELECT *` expands to every column in the FROM table or join. It cannot be combined with `GROUP BY` or aggregates.
- `SELECT` without `FROM` returns one row, for example `SELECT 1 + 1 AS two;`.
- `ORDER BY` accepts an expression, an output alias, or the name of an output column. A name that matches two output
  columns is an error. NULLs sort last in `ASC` and first in `DESC`.
- `LIMIT` and `OFFSET` take non-negative integer literals and may appear in either order.
- `SHOW TABLES` returns one column, `table_name`, sorted by name.

### Expressions and operators

| Kind        | Operators |
|-------------|-----------|
| Arithmetic  | `+ - * /` on integers and reals. Integer division truncates toward zero. |
| Unary       | `+` and `-` on numbers. |
| Text        | `||` concatenates two text values. |
| Comparison  | `= <> != < <= > >=`. Numbers compare with numbers, text with text, booleans with booleans. |
| Logic       | `AND`, `OR`, `NOT`, using three-valued logic. |
| NULL test   | `IS NULL`, `IS NOT NULL`. |

Any operator given NULL returns NULL, except `AND`, `OR`, `NOT` and the `IS NULL` tests. A `WHERE` clause keeps a row
only when its condition is exactly `TRUE`.

Integer arithmetic that overflows is an error. Division by zero is an error.

### Aggregates

| Function      | Result |
|---------------|--------|
| `COUNT(*)`    | Number of rows. |
| `COUNT(x)`    | Number of non-NULL values of `x`. |
| `SUM(x)`      | Sum of numbers. Integer sums stay integers and raise an error on overflow. NULL when there are no non-NULL values. |
| `AVG(x)`      | Average of numbers, as a `REAL`. NULL when there are no non-NULL values. |
| `MIN(x)`, `MAX(x)` | Smallest or largest non-NULL value of a type that orders with itself. |

Without `GROUP BY`, an aggregate query returns one row even for an empty table.

### Examples

```sql
CREATE TABLE sales (region TEXT, amount INTEGER);
INSERT INTO sales VALUES ('east', 10), ('west', 5), ('east', 20), ('west', NULL);

SELECT region, COUNT(*) AS orders, SUM(amount) AS total
FROM sales
GROUP BY region
ORDER BY total DESC;

SELECT c.name, o.total
FROM customers AS c
INNER JOIN orders AS o ON o.customer_id = c.id
ORDER BY o.total;

SELECT id, name FROM people WHERE id = 2;   -- uses the primary-key index
```

## Not supported

These are rejected with an error rather than ignored:

- `DISTINCT`, `HAVING`, `UNION`, `EXCEPT`, `INTERSECT`
- `LEFT`, `RIGHT`, `FULL` and `CROSS` joins, and comma-separated table lists
- `LIKE`, `IN`, `BETWEEN`, subqueries, and scalar functions other than the aggregates above
- `ORDER BY` with a position number, and `GROUP BY` with anything other than column names
- Composite primary keys, column defaults, foreign keys, `ALTER TABLE`, and indexes other than the primary key

Type checking happens when a value is evaluated. An ill-typed expression in a statement that matches no rows
succeeds without an error. For example, `UPDATE t SET n = 'x' WHERE FALSE` succeeds on an `INTEGER` column.

## Performance notes

- `WHERE primary_key = literal` reads one row through an in-memory index.
- Other `WHERE` clauses scan the whole table.
- `INNER JOIN` is a nested loop.
- `UPDATE` and `DELETE` rebuild the table's row list. They cost time proportional to the table size.
- Every commit waits for the log to reach disk.

## Project layout

```text
NumoSql.slnx                  solution
src/NumoSql/                  engine
  Lexing/ Parsing/ Syntax/    SQL text to syntax tree
  Schema/ Storage/            tables, log, checkpoint
  Execution/                  expression evaluation and statements
  Transactions/               undo log for one transaction
  Database.cs, Session.cs     public API
src/NumoSql.Cli/              command-line shell
tests/NumoSql.Tests/          xUnit tests
bench/NumoSql.Bench/          timing program used for the figures in the project notes
```
