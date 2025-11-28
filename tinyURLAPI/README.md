A minimal URL shortening REST API built with ASP.NET Core.
Features
•	Shorten URLs with expiry and visit count tracking
•	Redirect to original URLs via short codes
•	List all shortened URLs
•	Delete short URLs
•	Swagger UI for API documentation
Endpoints
•	GET /api/shorturls — List all short URLs
•	POST /api/shorturls — Create a new short URL
•	DELETE /api/shorturls/{code} — Delete a short URL by code
•	GET /{shortCode} — Redirect to the original URL
Usage
1.	Run the API
Use Visual Studio to build and run the project.
2.	Access Swagger UI
Navigate to http://localhost:{port}/swagger/index.html for API documentation and testing.
3.	CORS
All origins, methods, and headers are allowed.
Dependencies
•	ASP.NET Core
•	Swagger (Swashbuckle)