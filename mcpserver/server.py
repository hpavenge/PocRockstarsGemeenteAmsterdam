from flask import Flask, jsonify

app = Flask(__name__)

@app.route("/weer_vandaag")
def weer_vandaag():
    return jsonify({"antwoord": "Het weer is vandaag mooi."})

if __name__ == "__main__":
    app.run(port=5001)
