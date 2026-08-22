# Optional AI field suggestions

The first version uses local pattern recognition as its primary path. When an OpenAI API key is present, it also submits compact sheet profiles through the Responses API.

The request includes:

- source sheet names;
- runtime-detected header rows;
- header text;
- up to two limited data rows;
- target table names and runtime-discovered headers.

The response is a structured list of:

- source sheet and field;
- target sheet and field;
- confidence; and
- rationale.

Only suggestions at or above `MinimumAutoApplyConfidence` are retained. AI failure or rate limiting does not prevent the local pattern engine from processing supported workbooks.
