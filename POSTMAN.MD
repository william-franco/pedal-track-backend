🔑 Auth (público)
📌 Registrar usuário

```
POST /api/auth/register

{
  "name": "William Franco",
  "email": "william@pedaltrack.com",
  "password": "123456"
}
```

📌 Login

```
POST /api/auth/login

{
  "email": "william@example.com",
  "password": "123456"
}
```

🔑 Resposta esperada:

```
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6...",
  "refreshToken": "6d98b4af-1c3d-4b2c-8d7d-91fa0a2e4e7a"
}
```

📌 Refresh Token

```
POST /api/auth/refresh

{
  "refreshToken": "6d98b4af-1c3d-4b2c-8d7d-91fa0a2e4e7a"
}
```

🚲 Bikes (protegido)
📌 Criar bike

```
POST /api/bikes
🔑 Header obrigatório:

Authorization: Bearer {accessToken}

Body:

{
  "brand": "Specialized",
  "model": "Rockhopper 2024"
}
```

📊 Usage Records (protegido)
📌 Criar registro de uso

```
POST /api/usage-records

{
  "bikeId": 1,
  "kmTravelled": 25.7
}
```

🚴 Rides (protegido)
📌 Criar pedalada

```
POST /api/rides

{
  "distanceKm": 42.5,
  "conditions": "seco"
}
```

🔧 Maintenance Alerts (protegido)
📌 Criar alerta de manutenção

```
POST /api/maintenance-alerts

{
  "bikeId": 1,
  "type": "corrente",
  "thresholdValue": 500
}
```

✅ Maintenance Checklist (protegido)
📌 Adicionar item na checklist

```
POST /api/maintenance-checklist

{
  "bikeId": 1,
  "item": "freios"
}
```

⚡ Fluxo recomendado no Postman

Registrar usuário → /api/auth/register

Login → /api/auth/login → copiar o accessToken

Nas próximas requisições (/api/bikes, /api/rides, etc.), adicionar o header:

Authorization: Bearer {accessToken}
