from flask import Flask, jsonify, request
import os, math, traceback
from txtai.embeddings import Embeddings
from PyPDF2 import PdfReader

app = Flask(__name__)

# Globale opslag van chunks, zodat we niet afhankelijk zijn van txtai's doc() API
DOCS = []  # elke entry: {"id": int, "text": str, "source": str, "chunk": int}

embeddings = Embeddings({"path": "sentence-transformers/all-MiniLM-L6-v2"})

def chunk_text(text, max_chars=1200, overlap=150):
    text = text.replace("\r", " ").replace("\n", " ")
    chunks = []
    start = 0
    while start < len(text):
        end = min(start + max_chars, len(text))
        chunks.append(text[start:end])
        if end == len(text):
            break
        start = end - overlap
        if start < 0:
            start = 0
    return [c.strip() for c in chunks if c.strip()]

def load_docs():
    docs_dir = "docs"
    data = []
    idx = 0
    if not os.path.isdir(docs_dir):
        os.makedirs(docs_dir, exist_ok=True)

    for file in os.listdir(docs_dir):
        if not file.lower().endswith(".pdf"):
            continue

        path = os.path.join(docs_dir, file)
        reader = PdfReader(path)
        pages = []
        for p in reader.pages:
            t = p.extract_text() or ""
            pages.append(t)
        full = "\n".join(pages)

        # chunk het document
        chunks = chunk_text(full, max_chars=1200, overlap=150)
        for ci, ch in enumerate(chunks):
            DOCS.append({"id": idx, "text": ch, "source": file, "chunk": ci})
            data.append((idx, ch, {"source": file, "chunk": ci}))
            idx += 1

    if data:
        embeddings.index(data)

# Index initialiseren (eenmalig)
if not os.path.exists("index"):
    load_docs()
    embeddings.save("index")
else:
    try:
        embeddings.load("index")
        # DOCS opnieuw opbouwen is nodig als je index herlaadt na restart.
        # Simpelste manier: re-indexeren (sneller voor POC): 
        DOCS.clear()
        os.remove("index")
        load_docs()
        embeddings.save("index")
    except Exception:
        # als load faalt, bouw opnieuw
        DOCS.clear()
        load_docs()
        embeddings.save("index")

@app.route("/weer_vandaag")
def weer_vandaag():
    return jsonify({"antwoord": "Het weer is vandaag mooi."})

@app.route("/zoek_in_documenten", methods=["GET"])
def zoek_in_documenten():
    try:
        query = request.args.get("q", "").strip()
        k = int(request.args.get("k", "3"))
        if not query:
            return jsonify({"error": "Geen query"}), 400

        results = embeddings.search(query, k)
        passages = []

        # results kan per versie list[dict] of list[tuple] zijn
        for r in results:
            if isinstance(r, dict):
                docid = r.get("id")
                score = r.get("score")
            elif isinstance(r, (list, tuple)) and len(r) >= 1:
                docid = r[0]
                score = r[1] if len(r) > 1 else None
            else:
                docid = int(r)
                score = None

            item = DOCS[docid] if 0 <= docid < len(DOCS) else None
            if item:
                passages.append({
                    "id": docid,
                    "passage": item["text"],
                    "source": item["source"],
                    "chunk": item["chunk"],
                    "score": float(score) if score is not None else None
                })

        return jsonify(passages)
    except Exception as e:
        # Log naar console zodat je in terminal ziet wat er mis ging
        traceback.print_exc()
        return jsonify({"error": str(e)}), 500

if __name__ == "__main__":
    app.run(port=5001)
