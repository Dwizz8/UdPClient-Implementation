# COMPX234-A3
PORT         ?= 51234
SERVER_PORT  ?= 61234
LOSS         ?= 20

.PHONY: help build run run-with-loss

help:
	@echo "COMPX234-A3 Makefile"
	@echo "  make build          - compile server and client"
	@echo "  make run            - build, then start server (no loss)"
	@echo "  make run-with-loss  - build, then start with proxy ($(LOSS)% loss)"
	@echo ""
	@echo "Variables: PORT=$(PORT) SERVER_PORT=$(SERVER_PORT) LOSS=$(LOSS)"
	@echo "  PORT: client connects here; SERVER_PORT: server binds here; LOSS: %% packets dropped"

build:
	@mcs -out:server/UdpFileServer.exe server/UdpFileServer.cs server/FileTransferWorker.cs
	@mcs -out:client/UdpFileClient.exe client/UdpFileClient.cs

run: build
	@echo "Running on port $(PORT)"
	@cd server && mono ./UdpFileServer.exe $(PORT)

run-with-loss: build
	@bash -c '\
		trap "kill $$(cat .proxy.pid 2>/dev/null) 2>/dev/null; rm -f .proxy.pid" EXIT INT TERM; \
		mono proxy/Proxy.exe $(PORT) $(SERVER_PORT) $(LOSS) & \
		echo $$! > .proxy.pid; sleep 0.5; \
		echo "Proxy: client port $(PORT) -> server port $(SERVER_PORT)"; \
		cd server && exec mono ./UdpFileServer.exe $(SERVER_PORT) \
	'
