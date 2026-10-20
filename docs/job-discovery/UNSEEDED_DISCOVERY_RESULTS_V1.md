# Unseeded Discovery Results V1

Ngày thử: 2026-10-20, Asia/Saigon.
Kết quả khóa lúc **2026-10-20T21:28:30.1093159+07:00**, trước khi truy cập Facebook reference hoặc website DTMED.

## Kết quả đã khóa

**UNSEEDED_DISCOVERY_FAIL — NO_GO cho target coverage trong mẫu thử này.**

Không có tên DTMED, domain, URL hoặc board ID trong request tìm JD. Người thực hiện đã biết ground truth từ task nên đây là kiểm tra input của cơ chế, không phải nghiên cứu mù độc lập.

Provider được chọn bằng nghiên cứu tài liệu: Himalayas public Search API. Search tương tác chỉ tìm tài liệu provider; không có JD từ search tương tác được tính vào kết quả. Không dùng các source/company đã xác minh ở các vòng trước làm seed.

## Cơ chế có thể chạy bằng HTTP client trên cloud

Himalayas công bố public Search API không cần key, free với attribution và giới hạn sử dụng. Đường dẫn: `GET https://himalayas.app/jobs/api/search?q={URL_ENCODED_QUERY}&sort=recent&page=1`.

Bằng chứng chính thức:

- [API và terms sử dụng](https://himalayas.app/api).
- [Reference](https://himalayas.app/docs/remote-jobs-api).
- [OpenAPI](https://himalayas.app/docs/openapi.json).

Đây là API remote jobs nên có bias coverage rõ ràng đối với việc onsite Hà Nội. Nó được chọn để kiểm tra một đường discovery hợp lệ, không đại diện toàn bộ thị trường. Attribution/link-back là bắt buộc; không đẩy listings lên aggregator bên thứ ba.

## Request ledger

| Request | Query q | totalCount | jobs trả về |
|---:|---|---:|---:|
| U1 | `.NET Junior Hanoi` | 0 | 0 |
| U2 | `C# Fresher Ha Noi` | 0 | 0 |
| U3 | `ASP.NET Core Intern Hanoi` | 0 | 0 |
| U4 | `Angular Junior Hà Nội` | 0 | 0 |
| U5 | `.NET Hanoi` | 0 | 0 |
| U6 | `C# Hanoi` | 0 | 0 |
| U7 | `Angular Hanoi` | 0 | 0 |
| U8 | `ASP.NET Core Ha Noi` | 0 | 0 |

U5–U8 bỏ seniority khỏi query nhằm tránh bỏ sót vì query quá hẹp. Không phân trang khi response rỗng. Không gọi AI/Gemini hoặc paid search.

## Metrics

| Metric | Giá trị |
|---|---:|
| Provider/API product mới được nghiên cứu | 1 |
| Company/career/ATS/feed source candidates từ payload | 0 |
| JD records thực nhận | 0 |
| JD unique | 0 |
| JD đúng technology + location + seniority | 0 |
| DTMED xuất hiện | Không |
| Duplicate records | 0 |
| Duplicate rate | N/A, mẫu rỗng; không báo 0% precision/duplicate rate |
| Outbound GET của vòng tự động thành công | 8 |
| Giới hạn vòng | 20 HTTP requests; không retry/redirect tự động trong thuật toán dự kiến |
| Chi phí provider phát sinh | $0 |

Lần chạy trong sandbox bị lỗi kết nối ở GET đầu tiên, trước response; dừng bằng Stop. Sau đó chạy lại với network permission và nhận đủ 8 response. Có thêm 1 lần cố gắng kết nối thất bại; tổng số lần thử client là 9, vẫn dưới 20. Không suy diễn lần lỗi đó đã đến server.

## Cloud và giới hạn kết luận

Contract public HTTPS có thể được gọi bằng curl/.NET trên GitHub Actions, không cần thiết bị cá nhân. Tuy nhiên chưa thực thi workflow trên GitHub-hosted runner: **CLOUD_AUTOMATION_UNVERIFIED**. Không dùng tool search của Codex làm runtime discovery dependency.

Quota anonymous chia sẻ với người dùng khác; không có numeric allowance riêng được xác minh. Nếu 429 phải dừng, không xoay proxy. Không tạo tài khoản/key hoặc phương thức thanh toán.

Để đạt $0, chỉ dùng standard runner thuộc free allowance của repository và hard-stop khi hết quota; không bật paid fallback. Spike không kiểm tra billing/account thực tế nên không bảo đảm cloud cost vô điều kiện.

Không tìm được JD phù hợp qua cơ chế đã chạy. Không có dữ liệu chứng minh DTMED không tồn tại trên Internet. Các REST/feed công ty cần domain từ bước discovery trước; polling NCC/DTMED biết sẵn sẽ là seeded. ATS tương tự cần board token. Chưa xác minh được general search API miễn phí không cần credentials đủ coverage Hà Nội; các service cần credentials/payment không được thử.

## Quy tắc khóa

Không bổ sung bất kỳ kết quả DTMED hoặc website được người dùng cung cấp vào metrics trên sau thời điểm khóa. Seeded verification được ghi riêng trong DTMED_DISCOVERY_TEST_V1.md.
