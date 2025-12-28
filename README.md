\# ConnectionRevitCloud



Windows-клиент (WPF) — оболочка для WireGuard:

\- логин/пароль -> запрос к серверу

\- получение WireGuard-конфига

\- подключение/отключение WireGuard tunnel service

\- статус, внутренний WG IP, диагностика (rx/tx, handshake, ping)

\- работа в фоне + tray



Сервер (Ubuntu 22.04):

\- HTTPS API на 443

\- /admin доступен только с 10.10.0.100 (админский WG IP)

\- SQLite база

\- управление users + (опционально) генерация конфигов и правка wg0.conf



\## Быстрый старт сервера

```bash

sudo apt-get update -y \&\& sudo apt-get install -y git

git clone https://github.com/<you>/ConnectionRevitCloud.git

cd ConnectionRevitCloud

sudo bash deploy/install\_server.sh



