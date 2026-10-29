FROM python:3.12-slim

RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    wimtools \
    p7zip-full \
    rsync \
    procps \
    docker.io \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /opt/pxe-panel

COPY panel/requirements.txt /opt/pxe-panel/requirements.txt
RUN pip install --no-cache-dir -r requirements.txt

COPY panel/app /opt/pxe-panel/app

EXPOSE 8080

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8080"]
