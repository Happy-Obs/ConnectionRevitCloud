#!/usr/bin/env bash
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "Запусти скрипт от root: sudo bash deploy/install_server.sh"
  exit 1
fi

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_ROOT="/opt/connectionrevitcloud"
APP_DIR="$APP_ROOT/app"
CERT_DIR="$APP_ROOT/certs"
DATA_DIR="$APP_ROOT/data"
CONFIGS_DIR="$APP_ROOT/configs"
DOWNLOADS_DIR="$APP_ROOT/downloads"
SETTINGS_PATH="$APP_DIR/appsettings.Production.json"
SERVICE_PATH="/etc/systemd/system/connectionrevitcloud.service"

prompt() {
  local var="$1"
  local text="$2"
  local def="${3:-}"
  local input=""
  if [[ -n "$def" ]]; then
    read -r -p "$text [$def]: " input
    input="${input:-$def}"
  else
    read -r -p "$text: " input
  fi
  printf -v "$var" "%s" "$input"
}

echo "=== ConnectionRevitCloud Server Installer (Ubuntu 22.04) ==="
echo

prompt VPS_IP "Внешний IP VPS (endpoint host)" "87.242.103.223"
prompt WG_INTERFACE "Имя WG интерфейса" "wg0"
prompt WG_CONFIG_PATH "Путь до серверного WG конфига" "/etc/wireguard/wg0.conf"
prompt WG_PORT "Порт WireGuard (UDP)" "36953"
prompt CLIENT_ALLOWED "AllowedIPs для клиентов" "10.10.0.0/24"
prompt KEEPALIVE "PersistentKeepalive" "25"
prompt ADMIN_IP "Внутренний IP админа (доступ к /admin)" "10.10.0.100"

echo
echo "Установка зависимостей..."
apt-get update -y
apt-get install -y curl ca-certificates openssl sqlite3 wireguard-tools git wget

echo
echo "Установка .NET SDK 8 (для сборки на сервере)..."
if ! command -v dotnet >/dev/null 2>&1; then
  wget -q https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
  dpkg -i /tmp/packages-microsoft-prod.deb
  rm -f /tmp/packages-microsoft-prod.deb
  apt-get update -y
  apt-get install -y dotnet-sdk-8.0
fi

echo
echo "Создание директорий в $APP_ROOT ..."
mkdir -p "$APP_DIR" "$CERT_DIR" "$DATA_DIR" "$CONFIGS_DIR" "$DOWNLOADS_DIR"

echo
echo "Генерация JWT ключа..."
JWT_KEY="$(openssl rand -base64 48 | tr -d '\n')"

echo
echo "Генерация пароля PFX..."
PFX_PASSWORD="$(openssl rand -base64 24 | tr -d '\n')"

echo
echo "Генерация self-signed сертификата на IP: $VPS_IP ..."
CRT_PATH="$CERT_DIR/server.crt"
KEY_PATH="$CERT_DIR/server.key"
PFX_PATH="$CERT_DIR/server.pfx"
if [[ -f "$PFX_PATH" && -f "$CRT_PATH" && -f "$KEY_PATH" ]]; then
  echo "Сертификат уже существует, пропускаем генерацию: $CRT_PATH"
else
  echo "Генерация self-signed сертификата на IP: $VPS_IP ..."
  openssl req -x509 -newkey rsa:4096 -sha256 -days 3650 -nodes \
    -keyout "$KEY_PATH" -out "$CRT_PATH" \
    -subj "/CN=ConnectionRevitCloud" \
    -addext "subjectAltName=IP:$VPS_IP"

  openssl pkcs12 -export -out "$PFX_PATH" -inkey "$KEY_PATH" -in "$CRT_PATH" -password "pass:$PFX_PASSWORD"
  chmod 600 "$PFX_PATH" "$KEY_PATH" "$CRT_PATH"
fi
echo
echo "SHA-256 fingerprint сертификата (нужно для pinning в клиенте):"
openssl x509 -in "$CRT_PATH" -noout -fingerprint -sha256

echo
echo "Генерация appsettings.Production.json ..."
TEMPLATE="$REPO_DIR/deploy/appsettings.template.json"

sed \
  -e "s|__PFX_PASSWORD__|$PFX_PASSWORD|g" \
  -e "s|__ADMIN_IP__|$ADMIN_IP|g" \
  -e "s|__JWT_KEY__|$JWT_KEY|g" \
  -e "s|__WG_INTERFACE__|$WG_INTERFACE|g" \
  -e "s|__WG_CONFIG_PATH__|$WG_CONFIG_PATH|g" \
  -e "s|__CLIENT_ALLOWED_IPS__|$CLIENT_ALLOWED|g" \
  -e "s|__ENDPOINT_HOST__|$VPS_IP|g" \
  -e "s|__ENDPOINT_PORT__|$WG_PORT|g" \
  -e "s|__KEEPALIVE__|$KEEPALIVE|g" \
  "$TEMPLATE" > "$SETTINGS_PATH"

chmod 600 "$SETTINGS_PATH"

echo
echo "Сборка и публикация сервера..."
pushd "$REPO_DIR/server/ConnectionRevitCloud.Server" >/dev/null
dotnet restore
dotnet publish -c Release -r linux-x64 --self-contained true -o "$APP_DIR"
popd >/dev/null

echo
echo "Установка systemd сервиса..."
sed \
  -e "s|/opt/connectionrevitcloud/app|$APP_DIR|g" \
  "$REPO_DIR/deploy/connectionrevitcloud.service.template" > "$SERVICE_PATH"

systemctl daemon-reload
systemctl enable connectionrevitcloud.service
systemctl restart connectionrevitcloud.service

echo
echo "Готово."
echo "- Сервер запущен: systemctl status connectionrevitcloud.service"
echo "- Настройки: $SETTINGS_PATH"
echo "- Сертификат: $CRT_PATH"
echo "- Конфиги клиентов: $CONFIGS_DIR"
echo "- Админка доступна ТОЛЬКО с IP $ADMIN_IP: https://$VPS_IP/admin"
echo
echo "ВАЖНО: открой 443/TCP для клиентов."
