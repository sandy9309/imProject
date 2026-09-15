import os
import re
import json
import time
import requests
import ikea_api

save_dir = "ikea_models"
os.makedirs(save_dir, exist_ok=True)

headers = {
    "User-Agent": "Mozilla/5.0"
}

# 專題用核心家具分類
keywords = [
    "chair",
    "sofa",
    "table",
    "storage",
    "bed",
    "desk"
]

# 每個關鍵字最多搜尋 1000 筆
SEARCH_LIMIT = 1000

constants = ikea_api.Constants(country="us", language="en")
downloaded_codes = set()


def safe_filename(text):
    if not text:
        return "unknown"

    text = text.strip()
    text = re.sub(r"[^a-zA-Z0-9_\u4e00-\u9fff]", "_", text)
    text = re.sub(r"_+", "_", text)

    return text.strip("_")


def find_glb(obj, urls):
    if isinstance(obj, dict):
        for key, value in obj.items():
            if key == "url" and isinstance(value, str) and ".glb" in value:
                urls.append(value)
            else:
                find_glb(value, urls)

    elif isinstance(obj, list):
        for item in obj:
            find_glb(item, urls)


def find_images(obj, urls):
    if isinstance(obj, dict):
        for key, value in obj.items():
            if isinstance(value, str):
                lower = value.lower()

                if (
                    ".jpg" in lower
                    or ".jpeg" in lower
                    or ".png" in lower
                    or ".webp" in lower
                ):
                    urls.append(value)

            find_images(value, urls)

    elif isinstance(obj, list):
        for item in obj:
            find_images(item, urls)


def find_product_name(obj):
    if isinstance(obj, dict):
        for key, value in obj.items():
            lower_key = key.lower()

            if lower_key in [
                "name",
                "productname",
                "product_name",
                "type_name",
                "displayname",
                "display_name",
                "title"
            ]:
                if isinstance(value, str) and len(value.strip()) > 0:
                    return value.strip()

        for value in obj.values():
            name = find_product_name(value)

            if name:
                return name

    elif isinstance(obj, list):
        for item in obj:
            name = find_product_name(item)

            if name:
                return name

    return None


def extract_products_from_search(obj, products):
    if isinstance(obj, dict):
        item_code = None

        for key, value in obj.items():
            if isinstance(value, str):
                nums = re.findall(r"\b\d{8}\b", value.replace(".", ""))

                if nums:
                    item_code = nums[0]
                    break

        if item_code:
            image_urls = []
            find_images(obj, image_urls)
            image_urls = list(dict.fromkeys(image_urls))

            product_name = find_product_name(obj)

            products.append({
                "code": item_code,
                "image": image_urls[0] if image_urls else None,
                "name": product_name
            })

        for value in obj.values():
            extract_products_from_search(value, products)

    elif isinstance(obj, list):
        for item in obj:
            extract_products_from_search(item, products)


def get_search_products(keyword, limit=1000):
    print("\n========================")
    print("搜尋關鍵字：", keyword)

    search = ikea_api.Search(constants)

    endpoint = search.search(
        keyword,
        limit=limit,
        types=["PRODUCT"]
    )

    result = ikea_api.run(endpoint)

    products = []
    extract_products_from_search(result, products)

    final_products = []
    seen = set()

    for p in products:
        code = p["code"]

        if code not in seen:
            seen.add(code)
            final_products.append(p)

    print("搜尋到商品數：", len(final_products))

    return final_products


def choose_best_glb(urls, target_code):
    urls = list(dict.fromkeys(urls))

    normal_urls = [
        u for u in urls
        if "/glb/" in u and "glb_draco" not in u
    ]

    draco_urls = [
        u for u in urls
        if "glb_draco" in u
    ]

    for u in normal_urls:
        if f"/{target_code}/" in u:
            return u

    if normal_urls:
        return normal_urls[0]

    for u in draco_urls:
        if f"/{target_code}/" in u:
            return u

    if draco_urls:
        return draco_urls[0]

    return None


def download_glb(item_code, base_filename):
    try:
        json_url = f"https://www.ikea.com/global/assets/rotera/resources/{item_code}.json"

        response = requests.get(
            json_url,
            headers=headers,
            timeout=(5, 15)
        )

        print("JSON 狀態碼：", response.status_code)

        if response.status_code != 200:
            return False, None

        data = response.json()

        urls = []
        find_glb(data, urls)
        urls = list(dict.fromkeys(urls))

        print("找到 GLB：", len(urls))

        if len(urls) == 0:
            return False, None

        model_url = choose_best_glb(urls, item_code)

        if model_url is None:
            return False, None

        print("下載模型：", model_url)

        r = requests.get(
            model_url,
            headers=headers,
            timeout=(5, 30)
        )

        print("模型狀態碼：", r.status_code)
        print("模型大小：", len(r.content))

        if r.status_code != 200 or len(r.content) < 1000:
            print("模型下載失敗")
            return False, model_url

        glb_path = os.path.join(save_dir, base_filename + ".glb")

        with open(glb_path, "wb") as f:
            f.write(r.content)

        print("模型已存：", glb_path)

        return True, model_url

    except Exception as e:
        print("模型錯誤：", e)
        return False, None


def save_product_info(info, base_filename):
    info_path = os.path.join(save_dir, base_filename + "_info.json")

    with open(info_path, "w", encoding="utf-8") as f:
        json.dump(info, f, ensure_ascii=False, indent=2)

    print("商品資料已存：", info_path)


def process_item(product, keyword):
    item_code = product["code"]
    image_url = product.get("image")
    product_name = product.get("name") or keyword

    if item_code in downloaded_codes:
        print("已下載過，跳過：", item_code)
        return False

    print("\n------------------------")
    print("商品：", item_code)

    clean_name = safe_filename(product_name)
    base_filename = f"{clean_name}_{item_code}"

    print("商品名稱：", product_name)
    print("統一檔名：", base_filename)

    model_success, model_url = download_glb(
        item_code,
        base_filename
    )

    if model_success:
        info = {
            "name": product_name,
            "category": keyword,
            "width": None,
            "length_cm": None,
            "height": None,
            "price": 0,
            "description": "",
            "image_url": image_url,
            "model_url": model_url
        }

        save_product_info(info, base_filename)

        downloaded_codes.add(item_code)

        return True

    return False


total_success = 0
total_fail = 0

for keyword in keywords:
    products = get_search_products(
        keyword,
        limit=SEARCH_LIMIT
    )

    success_count = 0
    fail_count = 0

    for product in products:
        success = process_item(
            product,
            keyword
        )

        if success:
            success_count += 1
            total_success += 1
        else:
            fail_count += 1
            total_fail += 1

        # 避免請求太快被擋
        time.sleep(0.5)

    print(f"\n{keyword} 完成")
    print("成功下載：", success_count)
    print("下載失敗：", fail_count)

print("\n========================")
print("全部完成")
print("成功模型數：", total_success)
print("失敗模型數：", total_fail)
print("總處理數：", total_success + total_fail)