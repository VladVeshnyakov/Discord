# --- Этап 1: Сборка ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Копируем csproj и восстанавливаем зависимости
COPY ["DiscordBirge.csproj", "./"]
RUN dotnet restore "DiscordBirge.csproj"

# Копируем всё остальное и публикуем
COPY . .
RUN dotnet publish "DiscordBirge.csproj" -c Release -o /app/publish /p:UseAppHost=false

# --- Этап 2: Запуск ---
# ВАЖНО: Используем aspnet, а не runtime, так как у тебя есть Web API (Контроллеры)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Копируем только собранные файлы
COPY --from=build /app/publish .

# Точка входа (имя dll должно совпадать с именем проекта)
ENTRYPOINT ["dotnet", "DiscordBirge.dll"]

