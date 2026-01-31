run:
	docker compose up --build -d

stop:
	docker compose down

clean-db:
	docker compose down -v

logs:
	docker compose logs -f
