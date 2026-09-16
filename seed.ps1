$ErrorActionPreference = "Stop"

try {
    $body = @{ email = "admin@tntsupermarket.com"; password = "Password@123" } | ConvertTo-Json
    $auth = Invoke-RestMethod -Uri "http://localhost:5001/api/auth/login" -Method POST -Body $body -ContentType "application/json"
    $token = $auth.accessToken
    $headers = @{ "Authorization" = "Bearer $token"; "Content-Type" = "application/json" }
    
    $catsRes = Invoke-RestMethod -Uri "http://localhost:5003/api/categories?includeInactive=false" -Method GET
    $catMap = @{}
    foreach ($cat in $catsRes.items) {
        $catMap[$cat.name] = $cat.id
    }
    
    Write-Host "Found $($catMap.Count) active categories."
    
    $products = @(
        # Groceries & Staples
        @{ Name = "Samba Rice 5kg"; Description = "High quality Samba rice in a 5kg bag."; Price = 1200; StockQuantity = 100; Unit = "bag"; CategoryId = $catMap["Groceries & Staples"] },
        @{ Name = "White Sugar 1kg"; Description = "Refined white sugar."; Price = 300; StockQuantity = 200; Unit = "packet"; CategoryId = $catMap["Groceries & Staples"] },
        @{ Name = "Red Dhal 1kg"; Description = "Premium quality red dhal."; Price = 450; StockQuantity = 150; Unit = "packet"; CategoryId = $catMap["Groceries & Staples"] },
        @{ Name = "Wheat Flour 1kg"; Description = "All-purpose wheat flour."; Price = 250; StockQuantity = 180; Unit = "packet"; CategoryId = $catMap["Groceries & Staples"] },
        @{ Name = "Cooking Oil 1L"; Description = "Vegetable cooking oil."; Price = 900; StockQuantity = 80; Unit = "bottle"; CategoryId = $catMap["Groceries & Staples"] },
        
        # Dairy & Beverages
        @{ Name = "Fresh Milk 1L"; Description = "Pasteurized fresh milk."; Price = 480; StockQuantity = 50; Unit = "carton"; CategoryId = $catMap["Dairy & Beverages"] },
        @{ Name = "Milk Powder 400g"; Description = "Full cream milk powder."; Price = 1100; StockQuantity = 120; Unit = "box"; CategoryId = $catMap["Dairy & Beverages"] },
        @{ Name = "Yoghurt 80ml"; Description = "Vanilla flavored yoghurt."; Price = 80; StockQuantity = 300; Unit = "cup"; CategoryId = $catMap["Dairy & Beverages"] },
        @{ Name = "Ceylon Tea 200g"; Description = "Premium Ceylon black tea."; Price = 550; StockQuantity = 100; Unit = "box"; CategoryId = $catMap["Dairy & Beverages"] },
        @{ Name = "Orange Juice 1L"; Description = "100% natural orange juice."; Price = 750; StockQuantity = 60; Unit = "carton"; CategoryId = $catMap["Dairy & Beverages"] },
    
        # Fruits & Vegetables
        @{ Name = "Red Apples 1kg"; Description = "Fresh imported red apples."; Price = 1800; StockQuantity = 40; Unit = "kg"; CategoryId = $catMap["Fruits & Vegetables"] },
        @{ Name = "Bananas 1kg"; Description = "Locally grown sweet bananas."; Price = 350; StockQuantity = 80; Unit = "kg"; CategoryId = $catMap["Fruits & Vegetables"] },
        @{ Name = "Mangoes 1kg"; Description = "Ripe and sweet mangoes."; Price = 800; StockQuantity = 30; Unit = "kg"; CategoryId = $catMap["Fruits & Vegetables"] },
        @{ Name = "Potatoes 1kg"; Description = "Fresh potatoes."; Price = 400; StockQuantity = 150; Unit = "kg"; CategoryId = $catMap["Fruits & Vegetables"] },
        @{ Name = "Tomatoes 1kg"; Description = "Fresh red tomatoes."; Price = 600; StockQuantity = 100; Unit = "kg"; CategoryId = $catMap["Fruits & Vegetables"] },
    
        # Meat, Seafood & Frozen
        @{ Name = "Chicken Breast 1kg"; Description = "Skinless, boneless chicken breast."; Price = 1900; StockQuantity = 50; Unit = "kg"; CategoryId = $catMap["Meat, Seafood & Frozen"] },
        @{ Name = "Chicken Sausages 500g"; Description = "Premium chicken sausages."; Price = 850; StockQuantity = 90; Unit = "pack"; CategoryId = $catMap["Meat, Seafood & Frozen"] },
        @{ Name = "Fresh Tuna 1kg"; Description = "Freshly caught yellowfin tuna."; Price = 2500; StockQuantity = 30; Unit = "kg"; CategoryId = $catMap["Meat, Seafood & Frozen"] },
        @{ Name = "Prawns 500g"; Description = "Medium-sized fresh prawns."; Price = 1600; StockQuantity = 40; Unit = "pack"; CategoryId = $catMap["Meat, Seafood & Frozen"] },
        @{ Name = "Frozen Mixed Vegetables 1kg"; Description = "Carrots, peas, corn, and beans."; Price = 1200; StockQuantity = 60; Unit = "pack"; CategoryId = $catMap["Meat, Seafood & Frozen"] },
    
        # Household & Personal Care
        @{ Name = "Shampoo 400ml"; Description = "Nourishing hair shampoo."; Price = 1150; StockQuantity = 70; Unit = "bottle"; CategoryId = $catMap["Household & Personal Care"] },
        @{ Name = "Bath Soap 100g"; Description = "Moisturizing bath soap."; Price = 150; StockQuantity = 250; Unit = "bar"; CategoryId = $catMap["Household & Personal Care"] },
        @{ Name = "Toothpaste 120g"; Description = "Fluoride toothpaste for cavity protection."; Price = 380; StockQuantity = 180; Unit = "tube"; CategoryId = $catMap["Household & Personal Care"] },
        @{ Name = "Dishwashing Liquid 500ml"; Description = "Tough on grease dishwashing liquid."; Price = 420; StockQuantity = 120; Unit = "bottle"; CategoryId = $catMap["Household & Personal Care"] },
        @{ Name = "Washing Powder 1kg"; Description = "Stain removal washing powder."; Price = 650; StockQuantity = 140; Unit = "pack"; CategoryId = $catMap["Household & Personal Care"] }
    )
    
    $successCount = 0
    $errors = @()
    
    foreach ($prod in $products) {
        if (-not $prod.CategoryId) {
            $errors += "Skipped $($prod.Name) - Category ID not found."
            continue
        }
        
        $payload = $prod | ConvertTo-Json
        try {
            $res = Invoke-RestMethod -Uri "http://localhost:5003/api/products" -Method POST -Headers $headers -Body $payload
            $successCount++
            Write-Host "✅ Created: $($prod.Name)"
        } catch {
            $status = $_.Exception.Response.StatusCode.value__
            if ($status -eq 409) {
                Write-Host "⏭️  Already exists: $($prod.Name)"
            } else {
                # Attempt to extract detailed response message
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $reader.BaseStream.Position = 0
                $responseBody = $reader.ReadToEnd()
                
                $err = "Failed ($status): $($prod.Name) - $responseBody"
                Write-Host "❌ $err"
                $errors += $err
            }
        }
    }
    
    Write-Host "Successfully created $successCount products."
    if ($errors.Count -gt 0) {
        Write-Host "Errors encountered:"
        $errors | ForEach-Object { Write-Host "  $_" }
    }
} catch {
    Write-Host "CRITICAL ERROR: $_"
}
