FROM python:3.13.14-slim-bookworm@sha256:67a1e1f215ccda113cfc024e8639049257e88f273898f595b61476d128d387e8

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    OPENBLAS_NUM_THREADS=1 \
    OMP_NUM_THREADS=1 \
    PHANTOM_TRANSCRIPTION_HOST=0.0.0.0 \
    PHANTOM_TRANSCRIPTION_WORKSPACE=/app \
    PHANTOM_TRANSCRIPTION_ENGINE=vosk \
    PHANTOM_TRANSCRIPTION_MODEL_PATH=/models/vosk-model-small-en-us-0.15 \
    PHANTOM_TRANSCRIPTION_TOKEN_FILE=/run/secrets/transcription_token

WORKDIR /app
COPY services/transcription/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt \
    && groupadd --gid 10001 speech \
    && useradd --uid 10001 --gid 10001 --no-create-home --shell /usr/sbin/nologin speech
COPY services/transcription/server.py services/transcription/recognizer.py services/transcription/healthcheck.py services/transcription/model.json ./
COPY services/transcription/accurate.py services/transcription/configuration.py services/transcription/diagnostics.py services/transcription/provision_whisper.py services/transcription/whisper-models.json services/transcription/technical-vocabulary.json ./

USER 10001:10001
EXPOSE 8765
HEALTHCHECK --interval=15s --timeout=3s --start-period=45s --retries=3 CMD ["python", "healthcheck.py"]
STOPSIGNAL SIGTERM
CMD ["python", "server.py"]
